using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WhatsAppBot.Services.Scraping
{
    /// <summary>
    /// IProductScraper implementation for one configured airline.
    ///
    /// Pipeline:
    ///   1) Bounded BFS crawl of the airline start URL (SafeCrawler — host-restricted,
    ///      depth/page-capped, polite delays).
    ///   2) Optional sitemap expansion: fetch a bounded number of sitemap URLs directly
    ///      (single-page fetches, no BFS) to cover JS-heavy sites.
    ///   3) Structured extraction with AirlineSiteScraper (routes, prices, offers,
    ///      booking URLs, images).
    ///
    /// Safety guarantees:
    ///   - NEVER throws: every failure path returns an (possibly empty) list and logs.
    ///   - NEVER fabricates data: if a page cannot be fetched/parsed it is skipped.
    ///   - Bounded: at most (MaxPagesPerSite + SitemapExpansionCount) HTTP fetches.
    /// </summary>
    public class AirlineProductScraper : Interfaces.IProductScraper
    {
        /// <summary>
        /// How many sitemap URLs (max) are fetched directly in addition to the BFS crawl.
        /// Kept small so a single sync run stays bounded and polite.
        /// </summary>
        public const int SitemapExpansionCount = 10;

        private readonly HttpClient _http;
        private readonly ILogger<AirlineProductScraper> _logger;
        private readonly ScrapingOptions _options;
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
            _options = options.Value;
            _target = target;
        }

        public async Task<List<ScrapedItem>> ScrapeAsync(CancellationToken ct)
        {
            try
            {
                if (!Uri.TryCreate(_target.StartUrl, UriKind.Absolute, out var startUrl))
                {
                    _logger.LogWarning(
                        "AirlineProductScraper[{Key}]: invalid StartUrl in config. Skipping.",
                        _target.SourceKey);
                    return new List<ScrapedItem>();
                }

                var allowedHost = string.IsNullOrWhiteSpace(_target.AllowedHost)
                    ? startUrl.Host
                    : _target.AllowedHost;

                var extractor = new AirlineSiteScraper(_logger);
                var results = new List<ScrapedItem>();

                // ── 1) Bounded BFS crawl from the start URL ─────────────────────
                try
                {
                    var crawler = new SafeCrawler(_http, _options);
                    var docs = await crawler.CrawlAsync(startUrl, allowedHost, ct);
                    results.AddRange(extractor.ExtractItemsFromDocuments(_target.Name, docs));
                }
                catch (OperationCanceledException)
                {
                    throw; // let cancellation propagate
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "AirlineProductScraper[{Key}]: BFS crawl failed for {Url}. Continuing.",
                        _target.SourceKey, startUrl);
                }

                // ── 2) Optional sitemap expansion (direct single-page fetches) ──
                if (!string.IsNullOrWhiteSpace(_target.SitemapUrl))
                {
                    try
                    {
                        var reader = new SitemapReader(_http);
                        var sitemapUrls = await reader.ReadUrlsAsync(_target.SitemapUrl, ct);

                        var candidates = sitemapUrls
                            .Where(u => string.Equals(u.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
                            .Take(SitemapExpansionCount)
                            .ToList();

                        foreach (var url in candidates)
                        {
                            ct.ThrowIfCancellationRequested();

                            try
                            {
                                var doc = await FetchDocumentAsync(url, ct);
                                if (doc == null) continue;
                                results.AddRange(
                                    extractor.ExtractItemsFromDocuments(_target.Name, new List<(Uri, IDocument)> { (url, doc) }));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(
                                    ex,
                                    "AirlineProductScraper[{Key}]: sitemap page fetch failed for {Url}",
                                    _target.SourceKey, url);
                            }

                            if (_options.DelayBetweenRequestsMs > 0)
                                await Task.Delay(_options.DelayBetweenRequestsMs, ct);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "AirlineProductScraper[{Key}]: sitemap read failed. Continuing without sitemap data.",
                            _target.SourceKey);
                    }
                }

                // ── 3) Deduplicate by source URL (stable for catalog upserts) ──
                var deduped = results
                    .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                    .GroupBy(i => i.SourceUrl, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Take(_options.MaxPagesPerSite)
                    .ToList();

                _logger.LogInformation(
                    "AirlineProductScraper[{Key}]: extracted {Count} item(s) for {Airline}",
                    _target.SourceKey, deduped.Count, _target.Name);

                return deduped;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Graceful failure: catalog sync must never crash the application.
                _logger.LogError(
                    ex,
                    "AirlineProductScraper[{Key}]: scrape failed. Returning empty result.",
                    _target.SourceKey);
                return new List<ScrapedItem>();
            }
        }

        /// <summary>
        /// Fetch a single page and parse it with AngleSharp. Returns null on any failure.
        /// </summary>
        private async Task<IDocument?> FetchDocumentAsync(Uri url, CancellationToken ct)
        {
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug(
                        "AirlineProductScraper[{Key}]: {Status} fetching {Url}",
                        _target.SourceKey, (int)response.StatusCode, url);
                    return null;
                }

                var html = await response.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var context = BrowsingContext.New(Configuration.Default);
                return await context.OpenAsync(req => req.Content(html).Address(url), ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "AirlineProductScraper[{Key}]: fetch error for {Url}", _target.SourceKey, url);
                return null;
            }
        }
    }
}
