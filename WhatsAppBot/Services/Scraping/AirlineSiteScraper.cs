using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;

namespace WhatsAppBot.Services.Scraping
{
    /// <summary>
    /// THE MOST ADVANCED AIRLINE SCRAPER EVER BUILT
    /// Extracts: routes, prices, offers, booking URLs, images, structured data
    /// </summary>
    public class AirlineSiteScraper
    {
        private readonly ILogger<AirlineSiteScraper>? _logger;

        public AirlineSiteScraper(ILogger<AirlineSiteScraper>? logger = null)
        {
            _logger = logger;
        }

        public List<ScrapedItem> ExtractItemsFromDocuments(
            string airlineName,
            List<(Uri Url, IDocument Doc)> docs)
        {
            var results = new List<ScrapedItem>();

            foreach (var (url, doc) in docs)
            {
                if (doc == null) continue;

                try
                {
                    var item = ExtractItemFromDocument(airlineName, url, doc);
                    if (item != null && !string.IsNullOrWhiteSpace(item.Name))
                        results.Add(item);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to extract item from {Url}", url);
                }
            }

            return results
                .GroupBy(x => x.SourceUrl, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(x => x.HasPrices ? 1 : 0)
                .ThenByDescending(x => x.HasRoutes ? 1 : 0)
                .ThenBy(x => x.Name)
                .ToList();
        }

        private ScrapedItem? ExtractItemFromDocument(string airlineName, Uri url, IDocument doc)
        {
            var title = CleanText(doc.Title);
            if (string.IsNullOrWhiteSpace(title))
                title = airlineName;

            // Clean title
            foreach (var suffix in new[] { "|", "-", "–", ":", "»", "•" })
            {
                var index = title.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                    title = title[..index].Trim();
            }

            var snippet = ExtractSnippet(doc);
            var prices = ExtractPrices(doc);
            var routes = ExtractRoutes(doc);
            var offers = ExtractOffers(doc);
            var bookUrl = FindBestBookNowUrl(doc, url);
            var images = ExtractImages(doc, url);
            var structured = ExtractStructuredData(doc);

            var enhancedSnippet = BuildEnhancedSnippet(snippet, prices, routes, offers, structured);

            return new ScrapedItem
            {
                Name = title,
                SourceUrl = url.ToString(),
                Snippet = enhancedSnippet,
                BookNowUrl = bookUrl,
                ImageUrls = images,
                Prices = prices,
                Routes = routes,
                Offers = offers,
                AirlineName = airlineName,
                PageType = DeterminePageType(url, doc),
                ExtractedAt = DateTime.UtcNow
            };
        }

        private string ExtractSnippet(IDocument doc)
        {
            var candidates = new[]
            {
                doc.QuerySelector("meta[property='og:description']")?.GetAttribute("content"),
                doc.QuerySelector("meta[name='description']")?.GetAttribute("content"),
                doc.QuerySelector("meta[name='twitter:description']")?.GetAttribute("content")
            };

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return CleanText(candidate)!;
            }

            var paras = doc.QuerySelectorAll("p")
                .Select(p => CleanText(p.TextContent))
                .Where(t => t?.Length > 50 && t?.Length < 500)
                .FirstOrDefault();

            return paras ?? string.Empty;
        }

        private List<string> ExtractPrices(IDocument doc)
        {
            var prices = new List<string>();
            var html = doc.DocumentElement?.TextContent ?? string.Empty;

            var patterns = new[]
            {
                @"(?:from|starting|as low as)\s*([₦$£€]\s*[\d,]+(?:\.\d{2})?)",
                @"([₦$£€]\s*[\d,]+(?:\.\d{2})?)",
                @"([\d,]+(?:\.\d{2})?\s*(?:USD|NGN|GBP|EUR|NGN))",
                @"price[^>]*>([^<]+?[\d,]+[^<]*)</",
                @"fare[^>]*>([^<]+?[\d,]+[^<]*)</"
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
                foreach (Match m in matches)
                {
                    var price = CleanText(m.Groups[1].Value)?.Trim();
                    if (!string.IsNullOrWhiteSpace(price) && price.Length < 50)
                    {
                        var formatted = price.Replace(" ", "");
                        if (!prices.Contains(formatted, StringComparer.OrdinalIgnoreCase))
                            prices.Add(formatted);
                    }
                }
            }

            // Element-based extraction
            var priceElements = doc.QuerySelectorAll(
                "[class*='price'], [class*='fare'], [class*='cost'], [class*='amount'], [data-price]");

            foreach (var el in priceElements.Take(10))
            {
                var text = CleanText(el.TextContent);
                if (text != null && Regex.IsMatch(text, @"[\d,]+") && text.Length < 50)
                {
                    if (!prices.Contains(text))
                        prices.Add(text);
                }
            }

            return prices.Distinct().Take(10).ToList();
        }

        private List<string> ExtractRoutes(IDocument doc)
        {
            var routes = new List<string>();
            var text = doc.DocumentElement?.TextContent ?? string.Empty;

            // IATA routes: LOS → LHR
            var iataMatches = Regex.Matches(text, @"\b([A-Z]{3})\s*(?:→|->|to|-|→)\s*([A-Z]{3})\b");
            foreach (Match m in iataMatches)
            {
                var route = $"{m.Groups[1].Value} → {m.Groups[2].Value}";
                if (!routes.Contains(route))
                    routes.Add(route);
            }

            // City routes
            var cityMatches = Regex.Matches(text, @"\b(\w+(?:\s+\w+)?)\s+(?:to|-)\s+(\w+(?:\s+\w+)?)\b");
            foreach (Match m in cityMatches)
            {
                var route = $"{m.Groups[1].Value} → {m.Groups[2].Value}";
                if (!routes.Contains(route))
                    routes.Add(route);
            }

            return routes.Distinct().Take(10).ToList();
        }

        private List<string> ExtractOffers(IDocument doc)
        {
            var offers = new List<string>();
            var offerSelectors = new[]
            {
                "[class*='offer']", "[class*='deal']", "[class*='promo']", "[class*='discount']",
                "[class*='sale']", "[class*='special']", "[class*='campaign']"
            };

            foreach (var selector in offerSelectors)
            {
                foreach (var el in doc.QuerySelectorAll(selector).Take(5))
                {
                    var text = CleanText(el.TextContent);
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 10 && text.Length < 200)
                    {
                        if (!offers.Contains(text))
                            offers.Add(text);
                    }
                }
            }

            return offers.Distinct().Take(5).ToList();
        }

        private Dictionary<string, string> ExtractStructuredData(IDocument doc)
        {
            var data = new Dictionary<string, string>();
            var scripts = doc.QuerySelectorAll("script[type='application/ld+json']");

            foreach (var script in scripts)
            {
                var json = script.TextContent;
                if (string.IsNullOrWhiteSpace(json)) continue;

                var nameMatch = Regex.Match(json, @"""name""\s*:\s*""([^""]+)""");
                if (nameMatch.Success)
                    data["name"] = nameMatch.Groups[1].Value;

                var priceMatch = Regex.Match(json, @"""price""\s*:\s*""?([^"",}]+)""?");
                if (priceMatch.Success)
                    data["price"] = priceMatch.Groups[1].Value;
            }

            return data;
        }

        private List<string> ExtractImages(IDocument doc, Uri baseUrl)
        {
            var images = new List<string>();

            // Priority: og:image
            var ogImg = doc.QuerySelector("meta[property='og:image']")?.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(ogImg))
                images.Add(NormalizeUrl(baseUrl, ogImg));

            // Hero images
            var heroSelectors = new[]
            {
                "[class*='hero'] img", "[class*='banner'] img", "[class*='header'] img",
                ".featured-image img", "[data-bg] img"
            };

            foreach (var selector in heroSelectors)
            {
                foreach (var img in doc.QuerySelectorAll(selector).Take(2))
                {
                    var src = img.GetAttribute("src") ??
                              img.GetAttribute("data-src") ??
                              img.GetAttribute("data-lazy-src");
                    if (!string.IsNullOrWhiteSpace(src))
                    {
                        var url = NormalizeUrl(baseUrl, src);
                        if (!images.Contains(url))
                            images.Add(url);
                    }
                }
            }

            // Regular images
            var regular = doc.QuerySelectorAll("img")
                .Select(i => i.GetAttribute("src") ?? i.GetAttribute("data-src"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => NormalizeUrl(baseUrl, s!))
                .Where(s => s.StartsWith("https://") && !s.Contains("logo") && !s.Contains("icon"))
                .Distinct()
                .Take(6);

            foreach (var img in regular)
                if (!images.Contains(img))
                    images.Add(img);

            return images.Take(6).ToList();
        }

        private string BuildEnhancedSnippet(string baseSnippet, List<string> prices, List<string> routes, List<string> offers, Dictionary<string, string> data)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(baseSnippet))
                sb.AppendLine(baseSnippet);

            if (routes.Count > 0)
                sb.AppendLine($"✈️ Routes: {string.Join(" • ", routes.Take(4))}");

            if (prices.Count > 0)
                sb.AppendLine($"💰 Prices from: {string.Join(" | ", prices.Take(3))}");

            if (offers.Count > 0)
                sb.AppendLine($"🎯 Offers: {offers[0]}");

            return sb.ToString().Trim();
        }

        private static string DeterminePageType(Uri url, IDocument doc)
        {
            var path = url.AbsolutePath.ToLowerInvariant();
            var title = (doc.Title ?? string.Empty).ToLowerInvariant();

            if (path.Contains("flight") || path.Contains("book") || title.Contains("book"))
                return "booking";
            if (path.Contains("destination") || path.Contains("route"))
                return "destination";
            if (path.Contains("offer") || path.Contains("deal") || path.Contains("promo"))
                return "promotion";
            if (path.Contains("schedule") || path.Contains("timetable"))
                return "schedule";
            if (path == "/" || path.Contains("home"))
                return "homepage";

            return "general";
        }

        private static string? FindBestBookNowUrl(IDocument doc, Uri pageUrl)
        {
            var links = doc.QuerySelectorAll("a")
                .Select(a => new
                {
                    Text = CleanText(a.TextContent)?.ToLowerInvariant() ?? string.Empty,
                    Href = a.GetAttribute("href")?.Trim() ?? string.Empty,
                    Class = (a.GetAttribute("class") ?? string.Empty).ToLowerInvariant()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Href) &&
                            !x.Href.StartsWith("#") &&
                            !x.Href.StartsWith("javascript:"))
                .ToList();

            var strongKeywords = new[] { "book now", "book flight", "search flights", "find flights", "reserve", "book ticket" };
            var partialKeywords = new[] { "book", "flight", "reserve", "ticket", "search" };

            var strongMatch = links.FirstOrDefault(x => strongKeywords.Any(k => x.Text.Contains(k)));
            if (strongMatch != null)
                return NormalizeUrl(pageUrl, strongMatch.Href);

            var buttonMatch = links.FirstOrDefault(x =>
                (x.Class.Contains("btn") || x.Class.Contains("button")) &&
                partialKeywords.Any(k => x.Text.Contains(k)));

            if (buttonMatch != null)
                return NormalizeUrl(pageUrl, buttonMatch.Href);

            var partialMatch = links.FirstOrDefault(x => partialKeywords.Any(k => x.Text.Contains(k)));
            if (partialMatch != null)
                return NormalizeUrl(pageUrl, partialMatch.Href);

            return null;
        }

        private static string CleanText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return Regex.Replace(text.Trim(), @"\s+", " ");
        }

        private static string NormalizeUrl(Uri baseUrl, string maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative))
                return baseUrl.ToString();

            if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out var abs))
                return abs.ToString();

            if (Uri.TryCreate(baseUrl, maybeRelative, out var rel))
                return rel.ToString();

            return baseUrl.ToString();
        }
    }
}