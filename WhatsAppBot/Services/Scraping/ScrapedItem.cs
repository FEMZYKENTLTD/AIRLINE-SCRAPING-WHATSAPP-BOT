using System.Collections.Generic;

namespace WhatsAppBot.Services.Scraping
{
    /// <summary>
    /// Normalized scrape result.
    /// We keep it small and safe: title, url, snippet, image urls, and a CTA link.
    /// </summary>
    public class ScrapedItem
    {
        public string Name { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>
        /// "Book now" / CTA URL.
        /// We'll store this inside Product.Description as requested.
        /// </summary>
        public string? BookNowUrl { get; set; }

        public string? Snippet { get; set; }

        public List<string> ImageUrls { get; set; } = new();
    }
}
