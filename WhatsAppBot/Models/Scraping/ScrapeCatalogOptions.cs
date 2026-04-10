namespace WhatsAppBot.Models.Scraping
{
    /// <summary>
    /// Config-driven scraping options so we avoid hardcoding website structures.
    /// You will set these in appsettings.json.
    /// </summary>
    public class ScrapeCatalogOptions
    {
        /// <summary>Enable/disable scraping sync.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Catalog page to scrape (public URL).</summary>
        public string CatalogUrl { get; set; } = string.Empty;

        /// <summary>How often to sync (minutes).</summary>
        public int SyncEveryMinutes { get; set; } = 60;

        /// <summary>Timeout for HTTP requests.</summary>
        public int HttpTimeoutSeconds { get; set; } = 20;

        /// <summary>Polite delay between requests (ms).</summary>
        public int RequestDelayMs { get; set; } = 700;

        /// <summary>User-Agent to identify your service politely.</summary>
        public string UserAgent { get; set; } = "FemzykCatalogBot/1.0 (+https://example.com)";

        // ----- CSS selectors -----

        /// <summary>Selector for each product card/container.</summary>
        public string ProductCardSelector { get; set; } = string.Empty;

        /// <summary>Selector (inside card) for product name.</summary>
        public string NameSelector { get; set; } = string.Empty;

        /// <summary>Selector (inside card) for product price.</summary>
        public string PriceSelector { get; set; } = string.Empty;

        /// <summary>Selector (inside card) for product SKU/ID (optional).</summary>
        public string? SkuSelector { get; set; }

        /// <summary>Selector (inside card) for product link (optional).</summary>
        public string? LinkSelector { get; set; }

        /// <summary>Selector (inside card) for product image URL (img tag).</summary>
        public string? ImageSelector { get; set; }

        /// <summary>If your price includes currency symbol, set this fallback currency.</summary>
        public string DefaultCurrency { get; set; } = "NGN";

        /// <summary>How to label this data source (e.g., "scrape").</summary>
        public string SourceName { get; set; } = "scrape";
    }
}
