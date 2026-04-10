using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models.Scraping;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Config-driven HTML scraper (generic).
    ///
    /// PURPOSE:
    /// - Scrape a public HTML page (CatalogUrl)
    /// - Select repeated "cards" using ProductCardSelector
    /// - Extract fields using CSS selectors (Name/Price/SKU/Image/Link)
    ///
    /// OUTPUT:
    /// Returns List<ScrapedItem> (NOT List<Product>).
    /// We normalize the result so the DB sync layer decides how to store it.
    ///
    /// WHY THIS FIX?
    /// Your IProductScraper interface expects List<ScrapedItem>.
    /// Your old file returned List<Product> which breaks DI + compilation.
    /// </summary>
    public class ConfigDrivenHtmlProductScraper : IProductScraper
    {
        private readonly HttpClient _http;
        private readonly ILogger<ConfigDrivenHtmlProductScraper> _logger;
        private readonly ScrapeCatalogOptions _opt;

        /// <summary>
        /// Unique identifier of this scraper's source.
        /// We use this as "SourceKey" so the DB layer can keep rows stable.
        /// </summary>
        public string SourceKey => string.IsNullOrWhiteSpace(_opt.SourceName) ? "scrape" : _opt.SourceName.Trim();

        public ConfigDrivenHtmlProductScraper(
            HttpClient http,
            IOptions<ScrapeCatalogOptions> options,
            ILogger<ConfigDrivenHtmlProductScraper> logger)
        {
            _http = http;
            _logger = logger;
            _opt = options.Value;
        }

        /// <summary>
        /// Scrape a catalog-like page and return normalized items.
        /// </summary>
        public async Task<List<ScrapedItem>> ScrapeAsync(CancellationToken ct)
        {
            // ----- Safety checks -----
            if (!_opt.Enabled)
            {
                _logger.LogInformation("ConfigDrivenHtmlProductScraper disabled via config.");
                return new List<ScrapedItem>();
            }

            // These are the minimum required selectors to scrape anything meaningful.
            if (string.IsNullOrWhiteSpace(_opt.CatalogUrl) ||
                string.IsNullOrWhiteSpace(_opt.ProductCardSelector) ||
                string.IsNullOrWhiteSpace(_opt.NameSelector))
            {
                _logger.LogWarning("Scraping config missing required fields. Check ScrapeCatalogOptions in appsettings.");
                return new List<ScrapedItem>();
            }

            var catalogUrl = _opt.CatalogUrl.Trim();
            _logger.LogInformation("Scraping catalog page: {Url}", catalogUrl);

            // ----- Polite HTTP defaults -----
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, _opt.HttpTimeoutSeconds));

            // NOTE: HttpClient.UserAgent is a request header; set once per call.
            _http.DefaultRequestHeaders.UserAgent.Clear();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_opt.UserAgent);

            // ----- Fetch HTML -----
            var html = await _http.GetStringAsync(catalogUrl, ct);

            // ----- Parse HTML (AngleSharp) -----
            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), ct);

            // ----- Select cards -----
            var cards = document.QuerySelectorAll(_opt.ProductCardSelector);
            _logger.LogInformation("Found {Count} card(s) using selector: {Selector}", cards.Length, _opt.ProductCardSelector);

            var results = new List<ScrapedItem>(cards.Length);

            foreach (var card in cards)
            {
                ct.ThrowIfCancellationRequested();

                // Name is the core field. If no name, item is useless.
                var name = TextOf(card, _opt.NameSelector);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Optional snippet: price text is not always a "price" for airlines,
                // but we can keep it as a snippet if available.
                var snippet = string.IsNullOrWhiteSpace(_opt.PriceSelector)
                    ? null
                    : TextOf(card, _opt.PriceSelector);

                // Link selector:
                // We treat this as the "Book now" (CTA) link.
                // Per your requirement, this will later be stored into Product.Description by the sync layer.
                var bookNowUrl = ExtractHref(card, _opt.LinkSelector, catalogUrl);

                // For SourceUrl:
                // Use the link if available, else fallback to the main catalog page.
                var sourceUrl = !string.IsNullOrWhiteSpace(bookNowUrl) ? bookNowUrl! : catalogUrl;

                // Image: optional
                var imageUrl = ExtractImageUrl(card, _opt.ImageSelector, catalogUrl);

                var item = new ScrapedItem
                {
                    Name = name.Trim(),
                    SourceUrl = sourceUrl.Trim(),
                    BookNowUrl = string.IsNullOrWhiteSpace(bookNowUrl) ? null : bookNowUrl!.Trim(),
                    Snippet = string.IsNullOrWhiteSpace(snippet) ? null : snippet!.Trim(),
                    ImageUrls = new List<string>()
                };

                if (!string.IsNullOrWhiteSpace(imageUrl))
                    item.ImageUrls.Add(imageUrl);

                results.Add(item);

                // Polite pacing (important if you later extend to follow detail pages)
                if (_opt.RequestDelayMs > 0)
                    await Task.Delay(_opt.RequestDelayMs, ct);
            }

            // Deduplicate by SourceUrl (stable + prevents duplicates if page repeats cards)
            var deduped = results
                .Where(x => !string.IsNullOrWhiteSpace(x.SourceUrl))
                .GroupBy(x => x.SourceUrl, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            _logger.LogInformation("Scrape completed. Returning {Count} normalized item(s).", deduped.Count);
            return deduped;
        }

        // -----------------------------
        // Helpers
        // -----------------------------

        /// <summary>
        /// Reads text content from inside a root element using selector.
        /// </summary>
        private static string TextOf(IElement root, string selector)
        {
            if (string.IsNullOrWhiteSpace(selector)) return string.Empty;

            var el = root.QuerySelector(selector);
            return el?.TextContent?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Extracts a href (anchor link) from selector and makes it absolute.
        /// </summary>
        private static string? ExtractHref(IElement root, string? selector, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(selector)) return null;

            var el = root.QuerySelector(selector);
            if (el == null) return null;

            var href = el.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href)) return null;

            return MakeAbsoluteUrl(href.Trim(), baseUrl);
        }

        /// <summary>
        /// Extracts an image URL from selector. Works for <img src> or generic tags with src/href/text.
        /// </summary>
        private static string ExtractImageUrl(IElement root, string? selector, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(selector))
                return string.Empty;

            var el = root.QuerySelector(selector);
            if (el == null)
                return string.Empty;

            // Prefer <img src="">
            if (el is IHtmlImageElement img && !string.IsNullOrWhiteSpace(img.Source))
                return MakeAbsoluteUrl(img.Source.Trim(), baseUrl);

            // Try src attribute
            var src = el.GetAttribute("src");
            if (!string.IsNullOrWhiteSpace(src))
                return MakeAbsoluteUrl(src.Trim(), baseUrl);

            // Try href attribute
            var href = el.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(href))
                return MakeAbsoluteUrl(href.Trim(), baseUrl);

            // Fallback: text content
            var text = el.TextContent?.Trim();
            return string.IsNullOrWhiteSpace(text) ? string.Empty : MakeAbsoluteUrl(text, baseUrl);
        }

        /// <summary>
        /// Converts relative URLs into absolute URLs using the page base URL.
        /// </summary>
        private static string MakeAbsoluteUrl(string url, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            // Already absolute
            if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
                return abs.ToString();

            // Combine with base URL
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
                Uri.TryCreate(baseUri, url, out var combined))
                return combined.ToString();

            // Worst-case fallback
            return url;
        }
    }
}
