using System;
using System.Linq;
using WhatsAppBot.Models;

namespace WhatsAppBot.Data
{
    public static class DbSeeder
    {
        public static void SeedDemoProducts(AppDbContext db)
        {
            if (db.Products.Any()) return;

            var p1 = new Product
            {
                Sku = "SKU-001",
                Name = "Chocolate Bento Cake",
                Description = "Small but premium. Perfect for surprises.",
                Price = 8000,
                Currency = "NGN",
                StockQty = 12,
                Source = "seed",
                LastSyncedAtUtc = DateTime.UtcNow,
                Images =
                {
                    new ProductImage
                    {
                        Url = "https://picsum.photos/seed/cake1/800/800",
                        IsPrimary = true,
                        Caption = "Chocolate Bento Cake"
                    }
                }
            };

            var p2 = new Product
            {
                Sku = "SKU-002",
                Name = "Money Bouquet",
                Description = "Luxury surprise bouquet setup.",
                Price = 25000,
                Currency = "NGN",
                StockQty = 3,
                Source = "seed",
                LastSyncedAtUtc = DateTime.UtcNow,
                Images =
                {
                    new ProductImage
                    {
                        Url = "https://picsum.photos/seed/bouquet1/800/800",
                        IsPrimary = true,
                        Caption = "Money Bouquet"
                    }
                }
            };

            db.Products.AddRange(p1, p2);
            db.SaveChanges();
        }
    }
}
