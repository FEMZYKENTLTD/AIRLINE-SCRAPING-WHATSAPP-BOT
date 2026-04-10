using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;

namespace WhatsAppBot.Services.Scraping
{
    /// <summary>
    /// A polite, bounded crawler:
    /// - stays inside one allowed host
    /// - limits depth + max pages
    /// - extracts useful candidate pages
    ///
    /// IMPORTANT:
    /// This is NOT a "hacky" scraper. No login, no bypassing protections.
    /// Only public HTML.
    /// </summary>
    public class SafeCrawler
    {
        private readonly HttpClient _http;
        private readonly ScrapingOptions _opts;

        public SafeCrawler(HttpClient http, ScrapingOptions opts)
        {
            _http = http;
            _opts = opts;
        }

        public async Task<List<(Uri Url, IDocument Doc)>> CrawlAsync(
            Uri startUrl,
            string allowedHost,
            CancellationToken ct)
        {
            var results = new List<(Uri, IDocument)>();

            // BFS queue: (url, depth)
            var queue = new Queue<(Uri Url, int Depth)>();
            queue.Enqueue((startUrl, 0));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var config = Configuration.Default;

            while (queue.Count > 0 && results.Count < _opts.MaxPagesPerSite)
            {
                ct.ThrowIfCancellationRequested();

                var (url, depth) = queue.Dequeue();

                // Host restriction
                if (!string.Equals(url.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip duplicates
                var key = url.ToString();
                if (!seen.Add(key))
                    continue;

                // Skip non-HTML-ish URLs quickly
                if (LooksLikeBinaryAsset(url))
                    continue;

                // Fetch HTML
                var html = await GetStringSafeAsync(url, ct);
                if (string.IsNullOrWhiteSpace(html))
                    continue;

                // Parse HTML using AngleSharp
                var context = BrowsingContext.New(config);
                var doc = await context.OpenAsync(req => req.Content(html).Address(url), ct);

                results.Add((url, doc));

                // Stop expanding links if depth limit reached
                if (depth >= _opts.MaxDepth)
                    continue;

                // Extract internal links for further crawling
                foreach (var next in ExtractInternalLinks(doc, url))
                {
                    if (next.Host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase))
                        queue.Enqueue((next, depth + 1));
                }

                // Polite delay
                if (_opts.DelayBetweenRequestsMs > 0)
                    await Task.Delay(_opts.DelayBetweenRequestsMs, ct);
            }

            return results;
        }

        private async Task<string?> GetStringSafeAsync(Uri url, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                var res = await _http.SendAsync(req, ct);

                if (!res.IsSuccessStatusCode)
                    return null;

                var contentType = res.Content.Headers.ContentType?.MediaType ?? "";

                // Only accept HTML / text
                if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                    !contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
                    return null;

                return await res.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                return null;
            }
        }

        private static bool LooksLikeBinaryAsset(Uri url)
        {
            var path = url.AbsolutePath.ToLowerInvariant();
            return path.EndsWith(".jpg") || path.EndsWith(".jpeg") || path.EndsWith(".png") ||
                   path.EndsWith(".webp") || path.EndsWith(".gif") ||
                   path.EndsWith(".pdf") ||
                   path.EndsWith(".zip") || path.EndsWith(".rar") ||
                   path.EndsWith(".mp4") || path.EndsWith(".mp3");
        }

        private static IEnumerable<Uri> ExtractInternalLinks(IDocument doc, Uri baseUri)
        {
            // Grab hrefs from <a>
            foreach (var a in doc.QuerySelectorAll("a[href]"))
            {
                var href = a.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href))
                    continue;

                // Ignore anchors and JS pseudo-links
                if (href.StartsWith("#", StringComparison.Ordinal) ||
                    href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Uri.TryCreate(baseUri, href, out var uri))
                {
                    // Normalize by removing fragments
                    var clean = new UriBuilder(uri) { Fragment = "" }.Uri;
                    yield return clean;
                }
            }
        }
    }
}
