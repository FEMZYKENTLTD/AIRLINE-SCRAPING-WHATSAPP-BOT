using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WhatsAppBot.Services.Scraping
{
    /// <summary>
    /// Reads sitemap XML (urlset or sitemapindex).
    /// This is often the best way to discover pages on big/JS-heavy sites.
    /// </summary>
    public class SitemapReader
    {
        private readonly HttpClient _http;

        public SitemapReader(HttpClient http) => _http = http;

        public async Task<List<Uri>> ReadUrlsAsync(string sitemapUrl, CancellationToken ct)
        {
            var urls = new List<Uri>();

            var xml = await _http.GetStringAsync(sitemapUrl, ct);
            if (string.IsNullOrWhiteSpace(xml))
                return urls;

            var doc = XDocument.Parse(xml);

            // Handle sitemapindex -> nested sitemaps
            if (doc.Root?.Name.LocalName.Equals("sitemapindex", StringComparison.OrdinalIgnoreCase) == true)
            {
                foreach (var sm in doc.Descendants())
                {
                    if (sm.Name.LocalName.Equals("loc", StringComparison.OrdinalIgnoreCase))
                    {
                        var loc = sm.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(loc) && Uri.TryCreate(loc, UriKind.Absolute, out var nested))
                        {
                            // Pull nested sitemap URLs (bounded by reasonable limit)
                            var nestedUrls = await ReadUrlsAsync(loc, ct);
                            urls.AddRange(nestedUrls);
                        }
                    }
                }

                return urls;
            }

            // Handle urlset -> direct URLs
            foreach (var loc in doc.Descendants())
            {
                if (!loc.Name.LocalName.Equals("loc", StringComparison.OrdinalIgnoreCase))
                    continue;

                var val = loc.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(val) && Uri.TryCreate(val, UriKind.Absolute, out var u))
                    urls.Add(u);
            }

            return urls;
        }
    }
}
