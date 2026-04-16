using System;
using System.Collections.Generic;

namespace WhatsAppBot.Services.Scraping
{
    public class ScrapedItem
    {
        public string Name { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string? Snippet { get; set; }
        public string? BookNowUrl { get; set; }
        public List<string> ImageUrls { get; set; } = new();
        public string AirlineName { get; set; } = string.Empty;
        public string PageType { get; set; } = "general";
        public List<string> Prices { get; set; } = new();
        public List<string> Routes { get; set; } = new();
        public List<string> Offers { get; set; } = new();
        public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

        // Computed
        public bool HasPrices => Prices.Count > 0;
        public bool HasRoutes => Routes.Count > 0;
        public bool HasOffers => Offers.Count > 0;
        public bool HasImages => ImageUrls.Count > 0;
        public string PrimaryImage => ImageUrls.Count > 0 ? ImageUrls[0] : string.Empty;

        public override string ToString() =>
            $"[{AirlineName}] {Name} | Routes: {Routes.Count} | Prices: {Prices.Count} | {SourceUrl}";
    }
}