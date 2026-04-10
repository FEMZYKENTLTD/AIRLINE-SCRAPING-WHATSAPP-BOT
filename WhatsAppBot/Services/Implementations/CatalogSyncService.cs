using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Runs ALL registered IProductScraper implementations and upserts results into DB.
    ///
    /// Key rules (professional-grade):
    /// 1) Stable SKU per (sourceKey + sourceUrl) so records update instead of duplicating.
    /// 2) Description ALWAYS includes "Book now" URL (requested).
    /// 3) Images are replaced deterministically (max 5, HTTPS only).
    /// 4) Optional stale deactivation is SAFE: only deactivate if we successfully scraped items.
    /// </summary>
    public class CatalogSyncService : ICatalogSyncService
    {
        private readonly AppDbContext _db;
        private readonly IEnumerable<IProductScraper> _scrapers;
        private readonly ILogger<CatalogSyncService> _logger;

        public CatalogSyncService(
            AppDbContext db,
            IEnumerable<IProductScraper> scrapers,
            ILogger<CatalogSyncService> logger)
        {
            _db = db;
            _scrapers = scrapers;
            _logger = logger;
        }

        public async Task RunOnceAsync(CancellationToken ct)
        {
            // If there are no scrapers registered, don't silently pass.
            var scraperList = _scrapers?.ToList() ?? new List<IProductScraper>();
            if (scraperList.Count == 0)
            {
                _logger.LogWarning("CatalogSyncService: No scrapers registered. Nothing to sync.");
                return;
            }

            foreach (var scraper in scraperList)
            {
                ct.ThrowIfCancellationRequested();

                var sourceKey = scraper.SourceKey;

                _logger.LogInformation("CatalogSync: Running scraper for source: {SourceKey}", sourceKey);

                List<ScrapedItem> items;
                try
                {
                    items = await scraper.ScrapeAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CatalogSync: Scraper failed for source: {SourceKey}", sourceKey);
                    continue;
                }

                // Upsert items into DB for this source
                await UpsertForSourceAsync(sourceKey, items, ct);

                _logger.LogInformation(
                    "CatalogSync: Completed source {SourceKey}. Items scraped: {Count}",
                    sourceKey, items?.Count ?? 0);
            }
        }

        private async Task UpsertForSourceAsync(string sourceKey, List<ScrapedItem> items, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            items ??= new List<ScrapedItem>();

            // Load existing products for this source (tracked, so updates work)
            var existing = await _db.Products
                .Include(p => p.Images)
                .Where(p => p.Source == sourceKey)
                .ToListAsync(ct);

            var existingBySku = existing.ToDictionary(p => p.Sku, StringComparer.OrdinalIgnoreCase);
            var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                if (item == null) continue;

                var name = (item.Name ?? string.Empty).Trim();
                var sourceUrl = (item.SourceUrl ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sourceUrl))
                    continue;

                var sku = GenerateSku(sourceKey, sourceUrl);
                seenSkus.Add(sku);

                // =========================
                // Build Description (requested):
                // - include snippet if present
                // - include "Book now: <url>"
                // If no BookNowUrl, fall back to the source URL
                // =========================
                var snippet = (item.Snippet ?? string.Empty).Trim();
                var bookNow = (item.BookNowUrl ?? string.Empty).Trim();

                var descParts = new List<string>(capacity: 2);

                if (!string.IsNullOrWhiteSpace(snippet))
                    descParts.Add(snippet);

                if (!string.IsNullOrWhiteSpace(bookNow))
                    descParts.Add($"Book now: {bookNow}");
                else
                    descParts.Add($"Book now: {sourceUrl}");

                var description = string.Join("\n\n", descParts);

                // Normalize and filter image URLs
                var imageUrls = (item.ImageUrls ?? new List<string>())
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Select(u => u.Trim())
                    .Where(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();

                if (existingBySku.TryGetValue(sku, out var product))
                {
                    // -------- Update existing ----------
                    product.Name = name;
                    product.Description = description;
                    product.SourceUrl = sourceUrl;
                    product.IsActive = true;
                    product.LastSyncedAtUtc = now;

                    // Replace images deterministically
                    product.Images.Clear();
                    for (var i = 0; i < imageUrls.Count; i++)
                    {
                        product.Images.Add(new ProductImage
                        {
                            Url = imageUrls[i],
                            IsPrimary = i == 0
                        });
                    }
                }
                else
                {
                    // -------- Insert new ----------
                    var newProduct = new Product
                    {
                        Sku = sku,
                        Name = name,
                        Description = description,
                        Source = sourceKey,
                        SourceUrl = sourceUrl,
                        IsActive = true,
                        LastSyncedAtUtc = now,

                        // Price / Stock usually not available on airline marketing pages
                        Price = 0,
                        Currency = "NGN",
                        StockQty = 0
                    };

                    for (var i = 0; i < imageUrls.Count; i++)
                    {
                        newProduct.Images.Add(new ProductImage
                        {
                            Url = imageUrls[i],
                            IsPrimary = i == 0
                        });
                    }

                    _db.Products.Add(newProduct);
                }
            }

            // =========================
            // SAFE stale deactivation:
            // Only deactivate old items if we actually scraped SOME items.
            // If scrape failed and returned 0, we do NOT deactivate everything.
            // =========================
            if (items.Count > 0)
            {
                foreach (var p in existing)
                {
                    if (!seenSkus.Contains(p.Sku))
                    {
                        // Deactivate items not present in current scrape run
                        p.IsActive = false;
                        p.LastSyncedAtUtc = now;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Generates a stable SKU using:
        /// - sourceKey prefix
        /// - SHA256 hash of URL (shortened)
        ///
        /// Example:
        ///   airpeace-3F9A1C2D5B
        /// </summary>
        private static string GenerateSku(string sourceKey, string sourceUrl)
        {
            sourceKey = (sourceKey ?? "source").Trim().ToLowerInvariant();
            sourceUrl = (sourceUrl ?? "").Trim();

            // Hash URL to avoid extremely long SKUs
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sourceUrl));

            // Take first 10 hex chars (enough uniqueness for this use)
            var hex = Convert.ToHexString(bytes).ToLowerInvariant();
            var shortHash = hex.Length >= 10 ? hex[..10] : hex;

            // Keep SKU length reasonable
            return $"{sourceKey}-{shortHash}";
        }
    }
}
