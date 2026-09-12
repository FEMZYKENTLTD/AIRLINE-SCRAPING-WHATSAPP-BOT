using System.Collections.Generic;

namespace WhatsAppBot.Services.Scraping
{
    /// <summary>
    /// Strongly typed config for appsettings.json: "Scraping"
    /// </summary>
    public class ScrapingOptions
    {
        public bool Enabled { get; set; } = true;
        public bool RunOnStartup { get; set; } = true;
        public int IntervalMinutes { get; set; } = 360;

        public string UserAgent { get; set; } = "WhatsAppBotScraper/1.0";
        public int RequestTimeoutSeconds { get; set; } = 25;

        public int MaxPagesPerSite { get; set; } = 60;
        public int MaxDepth { get; set; } = 2;
        public int DelayBetweenRequestsMs { get; set; } = 350;

        public List<AirlineTarget> Airlines { get; set; } = new();
    }

    public class AirlineTarget
    {
        public string Name { get; set; } = string.Empty;
        public string SourceKey { get; set; } = string.Empty;
        public string StartUrl { get; set; } = string.Empty;
        public string AllowedHost { get; set; } = string.Empty;

        /// <summary>
        /// Optional sitemap URL (great for JS-heavy sites because links are discoverable).
        /// </summary>
        public string? SitemapUrl { get; set; }

        // ── Optional CSS selectors for the config-driven catalog scraper ──
        // When omitted, the scraper for this airline safely returns 0 items
        // (it never fabricates data). Provide selectors per site to enable it.

        /// <summary>CSS selector for each product/deal card on the catalog page.</summary>
        public string? ProductCardSelector { get; set; }

        /// <summary>CSS selector (inside the card) for the product name.</summary>
        public string? NameSelector { get; set; }

        /// <summary>CSS selector (inside the card) for price text (optional).</summary>
        public string? PriceSelector { get; set; }

        /// <summary>CSS selector (inside the card) for the "Book now" link (optional).</summary>
        public string? LinkSelector { get; set; }

        /// <summary>CSS selector (inside the card) for the image (optional).</summary>
        public string? ImageSelector { get; set; }
    }
}
