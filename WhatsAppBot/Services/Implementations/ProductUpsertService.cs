using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Data;
using WhatsAppBot.Models;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Upserts scraped products into DB using SKU as the unique key.
    /// - If product exists: update fields + refresh images
    /// - If not: insert new product
    /// </summary>
    public class ProductUpsertService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ProductUpsertService> _logger;

        public ProductUpsertService(AppDbContext db, ILogger<ProductUpsertService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task UpsertAsync(Product incoming, CancellationToken ct)
        {
            // Basic sanity
            if (string.IsNullOrWhiteSpace(incoming.Sku) || string.IsNullOrWhiteSpace(incoming.Name))
                return;

            // Load existing + images
            var existing = await _db.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Sku == incoming.Sku, ct);

            if (existing == null)
            {
                incoming.LastSyncedAtUtc = DateTime.UtcNow;
                _db.Products.Add(incoming);
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("Inserted new product {Sku} ({Name})", incoming.Sku, incoming.Name);
                return;
            }

            // Update fields
            existing.Name = incoming.Name;
            existing.Description = incoming.Description;
            existing.Price = incoming.Price;
            existing.Currency = incoming.Currency;
            existing.IsActive = incoming.IsActive;
            existing.Source = incoming.Source;
            existing.LastSyncedAtUtc = DateTime.UtcNow;

            // StockQty is optional; keep existing if you’re not scraping stock
            if (incoming.StockQty != 0)
                existing.StockQty = incoming.StockQty;

            // Refresh images (simple strategy: merge by URL, keep primary if any)
            foreach (var img in incoming.Images)
            {
                if (string.IsNullOrWhiteSpace(img.Url))
                    continue;

                var exists = existing.Images.Any(x => x.Url == img.Url);
                if (!exists)
                    existing.Images.Add(new ProductImage { Url = img.Url, IsPrimary = img.IsPrimary });
            }

            // Ensure at most one primary image
            if (existing.Images.Any(x => x.IsPrimary))
            {
                var firstPrimary = existing.Images.First(x => x.IsPrimary);
                foreach (var other in existing.Images.Where(x => x.Id != firstPrimary.Id))
                    other.IsPrimary = false;
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Updated product {Sku} ({Name})", existing.Sku, existing.Name);
        }
    }
}
