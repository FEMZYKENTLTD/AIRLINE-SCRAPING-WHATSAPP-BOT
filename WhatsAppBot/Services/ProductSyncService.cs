using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Implementations;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services
{
    /// <summary>
    /// ProductSyncService
    /// ------------------
    /// This is the background worker that keeps your DB updated automatically.
    ///
    /// IMPORTANT:
    /// Your IProductScraper interface is now:
    /// - SourceKey
    /// - ScrapeAsync(CancellationToken) -> List<ScrapedItem>
    ///
    /// This service runs all registered scrapers, converts ScrapedItem -> Product,
    /// and upserts into SQLite via ProductUpsertService.
    /// </summary>
    public class ProductSyncService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ProductSyncService> _logger;
        private readonly ScrapingOptions _opt;

        public ProductSyncService(
            IServiceProvider services,
            IOptions<ScrapingOptions> options,
            ILogger<ProductSyncService> logger)
        {
            _services = services;
            _logger = logger;
            _opt = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_opt.Enabled)
            {
                _logger.LogInformation("Product sync disabled (Scraping:Enabled=false).");
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, _opt.IntervalMinutes));

            _logger.LogInformation(
                "Product sync started. RunOnStartup={RunOnStartup}, Interval={Interval} minutes",
                _opt.RunOnStartup,
                interval.TotalMinutes);

            if (_opt.RunOnStartup)
                await RunOnceSafe(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken);
                    await RunOnceSafe(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("Product sync stopped.");
        }

        private async Task RunOnceSafe(CancellationToken ct)
        {
            try
            {
                await RunOnce(ct);
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Product sync run failed.");
            }
        }

        private async Task RunOnce(CancellationToken ct)
        {
            using var scope = _services.CreateScope();

            // Resolve all registered scrapers
            var scrapers = scope.ServiceProvider.GetServices<IProductScraper>().ToList();

            if (scrapers.Count == 0)
            {
                _logger.LogWarning("No IProductScraper registered. Nothing to sync.");
                return;
            }

            var upserter = scope.ServiceProvider.GetRequiredService<ProductUpsertService>();

            var totalUpserted = 0;

            foreach (var scraper in scrapers)
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogInformation("Running scraper: {SourceKey}", scraper.SourceKey);

                List<ScrapedItem> items;

                try
                {
                    items = await scraper.ScrapeAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scraper failed: {SourceKey}", scraper.SourceKey);
                    continue;
                }

                _logger.LogInformation("Scraper {SourceKey} returned {Count} item(s)", scraper.SourceKey, items.Count);

                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();

                    // Basic sanity
                    if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.SourceUrl))
                        continue;

                    var product = MapToProduct(scraper.SourceKey, item);

                    // Upsert into DB
                    await upserter.UpsertAsync(product, ct);
                    totalUpserted++;
                }
            }

            _logger.LogInformation("Product sync completed. Upserted {Count} record(s).", totalUpserted);
        }

        /// <summary>
        /// Converts a normalized ScrapedItem into your Product entity.
        /// We generate a stable SKU so future sync runs update the same row.
        ///
        /// Description requirement:
        /// - Store the “Book now” link as Description (plus snippet if available).
        /// </summary>
        private static Product MapToProduct(string sourceKey, ScrapedItem item)
        {
            var sku = GenerateStableSku(sourceKey, item.SourceUrl);

            // Build Description: snippet + BookNow (or fallback to SourceUrl)
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(item.Snippet))
                parts.Add(item.Snippet!.Trim());

            if (!string.IsNullOrWhiteSpace(item.BookNowUrl))
                parts.Add($"Book now: {item.BookNowUrl!.Trim()}");
            else
                parts.Add($"Book now: {item.SourceUrl.Trim()}");

            var description = string.Join("\n\n", parts);

            // Pick a primary image if available (public HTTPS only)
            var imgUrls = (item.ImageUrls ?? new List<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u) && u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            var product = new Product
            {
                Sku = sku,
                Name = item.Name.Trim(),
                Description = description,
                Source = sourceKey,
                SourceUrl = item.SourceUrl.Trim(),
                IsActive = true,
                LastSyncedAtUtc = DateTime.UtcNow,

                // Airlines usually don't expose stable public prices on all pages.
                // Keep these fields for compatibility/future.
                Currency = "NGN",
                Price = 0,
                StockQty = 0
            };

            for (var i = 0; i < imgUrls.Count; i++)
            {
                product.Images.Add(new ProductImage
                {
                    Url = imgUrls[i],
                    IsPrimary = i == 0
                });
            }

            return product;
        }

        /// <summary>
        /// Creates a stable SKU based on sourceKey + URL.
        /// This ensures future runs update instead of duplicating rows.
        /// </summary>
        private static string GenerateStableSku(string sourceKey, string url)
        {
            // Keep sourceKey safe for SKU usage
            var cleanSource = new string((sourceKey ?? "src")
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                .ToArray());

            if (string.IsNullOrWhiteSpace(cleanSource))
                cleanSource = "src";

            var input = $"{cleanSource}|{url}".ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            var hex = Convert.ToHexString(hash).ToLowerInvariant();

            // Short stable id
            return $"{cleanSource}-{hex[..12]}";
        }
    }
}
