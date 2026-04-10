using System;
using System.Collections.Generic;

namespace WhatsAppBot.Models
{
    /// <summary>
    /// A "Product" in this context is a catalog item the bot can show.
    ///
    /// For airlines, we treat public pages (offers/destinations/travel info) as "items"
    /// so the bot can return accurate info quickly from DB.
    /// </summary>
    public class Product
    {
        public int Id { get; set; }

        /// <summary>
        /// Stable SKU generated from SourceKey + URL hash so updates map to the same row.
        /// Example: airpeace-AB12CD34
        /// </summary>
        public string Sku { get; set; } = string.Empty;

        /// <summary>
        /// Display name/title scraped from the page/card.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// We will store "Book now" / primary CTA link inside Description (as requested),
        /// plus a short snippet if available.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Optional. Many airline public pages won't expose a numeric price reliably.
        /// Keep for future expansion.
        /// </summary>
        public decimal Price { get; set; }

        public string Currency { get; set; } = "NGN";

        /// <summary>
        /// For non-inventory websites, this is not meaningful. Keep for compatibility.
        /// </summary>
        public int StockQty { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Identifies where this record came from (e.g., airpeace, arikair, turkish, lufthansa).
        /// </summary>
        public string Source { get; set; } = "manual";

        /// <summary>
        /// The page that produced this item.
        /// </summary>
        public string SourceUrl { get; set; } = string.Empty;

        public DateTime LastSyncedAtUtc { get; set; } = DateTime.UtcNow;

        public List<ProductImage> Images { get; set; } = new();
    }
}
