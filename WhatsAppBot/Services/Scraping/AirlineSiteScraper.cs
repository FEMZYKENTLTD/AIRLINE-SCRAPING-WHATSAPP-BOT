using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;

namespace WhatsAppBot.Services.Scraping
{
    /// <summary>
    /// Extracts "items" from airline site pages.
    /// We treat useful pages/sections as catalog items:
    /// - Title (Name)
    /// - Page URL (SourceUrl)
    /// - "Book now" / CTA link (BookNowUrl) -> stored later inside Product.Description
    /// - Snippet (meta description / first paragraph)
    /// - Images (og:image + first few imgs)
    /// </summary>
    public class AirlineSiteScraper
    {
        public List<ScrapedItem> ExtractItemsFromDocuments(
            string airlineName,
            List<(Uri Url, IDocument Doc)> docs)
        {
            var results = new List<ScrapedItem>();

            foreach (var (url, doc) in docs)
            {
                if (doc == null) continue;

                var title = (doc.Title ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(title))
                    title = airlineName;

                // Try meta description for snippet
                var snippet = doc.QuerySelector("meta[name='description']")?.GetAttribute("content")?.Trim();
                if (string.IsNullOrWhiteSpace(snippet))
                {
                    // fallback: first non-empty paragraph
                    snippet = doc.QuerySelectorAll("p")
                        .Select(p => p.TextContent?.Trim())
                        .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                }

                // Find likely "Book" CTA
                var bookUrl = FindBestBookNowUrl(doc, url);

                // Images: og:image + a few <img src>
                var images = new List<string>();

                var ogImg = doc.QuerySelector("meta[property='og:image']")?.GetAttribute("content")?.Trim();
                if (!string.IsNullOrWhiteSpace(ogImg))
                    images.Add(NormalizeUrl(url, ogImg));

                var imgTags = doc.QuerySelectorAll("img")
                    .Select(i => i.GetAttribute("src"))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => NormalizeUrl(url, s!))
                    .Where(s => s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4);

                images.AddRange(imgTags);

                results.Add(new ScrapedItem
                {
                    Name = title,
                    SourceUrl = url.ToString(),
                    Snippet = snippet,
                    BookNowUrl = bookUrl,
                    ImageUrls = images
                });
            }

            // De-dupe by SourceUrl
            return results
                .GroupBy(x => x.SourceUrl, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static string? FindBestBookNowUrl(IDocument doc, Uri pageUrl)
        {
            // Look for anchors containing “book” / “booking” / “reserve”
            var links = doc.QuerySelectorAll("a")
                .Select(a => new
                {
                    Text = (a.TextContent ?? string.Empty).Trim(),
                    Href = (a.GetAttribute("href") ?? string.Empty).Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Href))
                .ToList();

            // Strong matches first
            var best = links.FirstOrDefault(x =>
                x.Text.Contains("book", StringComparison.OrdinalIgnoreCase) ||
                x.Text.Contains("booking", StringComparison.OrdinalIgnoreCase) ||
                x.Text.Contains("reserve", StringComparison.OrdinalIgnoreCase) ||
                x.Text.Contains("flight", StringComparison.OrdinalIgnoreCase));

            if (best == null) return null;

            return NormalizeUrl(pageUrl, best.Href);
        }

        private static string NormalizeUrl(Uri baseUrl, string maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative)) return baseUrl.ToString();

            // already absolute
            if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out var abs))
                return abs.ToString();

            // relative
            if (Uri.TryCreate(baseUrl, maybeRelative, out var rel))
                return rel.ToString();

            return baseUrl.ToString();
        }
    }
}
