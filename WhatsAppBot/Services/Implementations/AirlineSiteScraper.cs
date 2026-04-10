using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Scraping
{
    /// <summary>
    /// Implements IProductScraper using:
    /// - Sitemap (if configured) to discover pages, else
    /// - SafeCrawler for bounded internal discovery.
    /// Then turns pages into ScrapedItem list.
    /// </summary>
    public class AirlineProductScraper : IProductScraper
    {
        private readonly HttpClient _http;
        private readonly ILogger<AirlineProductScraper> _logger;
        private readonly ScrapingOptions _opts;
        private readonly AirlineTarget _target;

        public string SourceKey => _target.SourceKey;

        public AirlineProductScraper(
            HttpClient http,
            ILogger<AirlineProductScraper> logger,
            IOptions<ScrapingOptions> options,
            AirlineTarget target)
        {
            _http = http;
            _logger = logger;
            _opts = options.Value;
            _target = target;
        }

        public async Task<List<ScrapedItem>> ScrapeAsync(CancellationToken ct)
        {
            // Configure HTTP defaults per scraper run
            _http.Timeout = TimeSpan.FromSeconds(_opts.RequestTimeoutSeconds);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_opts.UserAgent);

            var airlineName = _target.Name;
            var start = new Uri(_target.StartUrl);

            var docs = new List<(Uri Url, AngleSharp.Dom.IDocument Doc)>();

            try
            {
                // If sitemap exists, use it to discover pages (bounded).
                if (!string.IsNullOrWhiteSpace(_target.SitemapUrl))
                {
                    _logger.LogInformation("Reading sitemap for {Airline}: {Sitemap}", airlineName, _target.SitemapUrl);

                    var sitemap = new SitemapReader(_http);
                    var urls = await sitemap.ReadUrlsAsync(_target.SitemapUrl!, ct);

                    // Restrict to allowed host + limit max pages
                    var filtered = urls
                        .Where(u => u.Host.Equals(_target.AllowedHost, StringComparison.OrdinalIgnoreCase))
                        .Take(_opts.MaxPagesPerSite)
                        .ToList();

                    _logger.LogInformation("Sitemap URLs selected for {Airline}: {Count}", airlineName, filtered.Count);

                    // Fetch and parse each URL as a document
                    var crawler = new SafeCrawler(_http, _opts);

                    foreach (var u in filtered)
                    {
                        ct.ThrowIfCancellationRequested();

                        // CrawlAsync returns documents, but for sitemap-based fetch we just want one page:
                        var singleDocs = await crawler.CrawlAsync(u, _target.AllowedHost, ct);

                        // Add only the first doc for that specific URL
                        var first = singleDocs.FirstOrDefault(x => x.Url.ToString() == u.ToString());
                        if (first.Doc != null)
                            docs.Add((first.Url, first.Doc));

                        if (_opts.DelayBetweenRequestsMs > 0)
                            await Task.Delay(_opts.DelayBetweenRequestsMs, ct);
                    }
                }
                else
                {
                    // Fallback: bounded internal crawl from start URL
                    _logger.LogInformation("Crawling site for {Airline}: {StartUrl}", airlineName, start);

                    var crawler = new SafeCrawler(_http, _opts);
                    docs.AddRange(await crawler.CrawlAsync(start, _target.AllowedHost, ct));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scrape failed for {Airline}", airlineName);
                return new List<ScrapedItem>();
            }

            // Extract items from collected documents
            var extractor = new AirlineSiteScraper();
            var items = extractor.ExtractItemsFromDocuments(airlineName, docs);

            _logger.LogInformation("Extracted {Count} items for {Airline}", items.Count, airlineName);

            return items;
        }
    }
}
