using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using WhatsAppBot.Services.CaptchaSolver;

namespace WhatsAppBot.Services.Scraping
{
    public class PlaywrightCrawler : IAsyncDisposable
    {
        private readonly ScrapingOptions _opts;
        private readonly ILogger<PlaywrightCrawler>? _logger;
        private readonly SelfCaptchaSolver? _captchaSolver;
        private StealthBrowser? _stealthBrowser;

        public PlaywrightCrawler(
            ScrapingOptions opts,
            ILogger<PlaywrightCrawler>? logger = null,
            SelfCaptchaSolver? captchaSolver = null)
        {
            _opts = opts;
            _logger = logger;
            _captchaSolver = captchaSolver;
        }

        public async Task<List<(Uri Url, IDocument Doc)>> CrawlAsync(
            Uri startUrl,
            string allowedHost,
            CancellationToken ct)
        {
            _stealthBrowser = new StealthBrowser(_logger);

            var results = new List<(Uri, IDocument)>();
            var queue = new Queue<(Uri Url, int Depth)>();
            queue.Enqueue((startUrl, 0));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (queue.Count > 0 && results.Count < _opts.MaxPagesPerSite)
            {
                ct.ThrowIfCancellationRequested();

                var (url, depth) = queue.Dequeue();

                if (!string.Equals(url.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seen.Add(url.ToString()))
                    continue;

                if (LooksLikeBinaryAsset(url))
                    continue;

                var html = await GetPageWithCaptchaSolving(url, ct);
                if (string.IsNullOrWhiteSpace(html))
                    continue;

                var config = Configuration.Default;
                var context = BrowsingContext.New(config);
                var doc = await context.OpenAsync(req => req.Content(html).Address(url), ct);

                results.Add((url, doc));

                if (depth >= _opts.MaxDepth)
                    continue;

                foreach (var next in ExtractInternalLinks(doc, url))
                {
                    if (next.Host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase))
                        queue.Enqueue((next, depth + 1));
                }

                if (_opts.DelayBetweenRequestsMs > 0)
                    await Task.Delay(_opts.DelayBetweenRequestsMs, ct);
            }

            return results;
        }

        private async Task<string?> GetPageWithCaptchaSolving(Uri url, CancellationToken ct)
        {
            try
            {
                await using var context = await _stealthBrowser!.CreateStealthContextAsync();
                var page = await context.NewPageAsync();

                page.SetDefaultTimeout(_opts.RequestTimeoutSeconds * 1000);

                var response = await page.GotoAsync(url.ToString(), new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = _opts.RequestTimeoutSeconds * 1000
                });

                if (response == null || !response.Ok)
                {
                    _logger?.LogWarning("Failed to load {Url}: {Status}", url, response?.Status);
                    return null;
                }

                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                // Use our CAPTCHA solver
                if (_captchaSolver != null)
                {
                    var captchaResult = await _captchaSolver.SolvePageChallengeAsync(page, ct);

                    if (captchaResult.Type != CaptchaType.None)
                    {
                        _logger?.LogInformation(
                            "CAPTCHA {Type} on {Url} - Solved: {Solved} - Method: {Method}",
                            captchaResult.Type, url, captchaResult.Solved, captchaResult.Method);

                        if (captchaResult.Solved && !string.IsNullOrEmpty(captchaResult.PageHtml))
                            return captchaResult.PageHtml;
                    }
                }

                // Dismiss cookie popups
                await DismissCookiePopups(page);

                return await page.ContentAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading {Url}", url);
                return null;
            }
        }

        private async Task DismissCookiePopups(IPage page)
        {
            var selectors = new[]
            {
                "button:has-text('Accept')", "button:has-text('Accept All')",
                "button:has-text('I Agree')", "button:has-text('OK')",
                "#onetrust-accept-btn-handler", ".cookie-accept",
                "[data-action='accept']"
            };

            foreach (var selector in selectors)
            {
                try
                {
                    var btn = await page.QuerySelectorAsync(selector);
                    if (btn != null && await btn.IsVisibleAsync())
                    {
                        await btn.ClickAsync();
                        await Task.Delay(500);
                        break;
                    }
                }
                catch { }
            }
        }

        private static bool LooksLikeBinaryAsset(Uri url)
        {
            var path = url.AbsolutePath.ToLowerInvariant();
            return path.EndsWith(".jpg") || path.EndsWith(".jpeg") || path.EndsWith(".png") ||
                   path.EndsWith(".webp") || path.EndsWith(".gif") || path.EndsWith(".pdf") ||
                   path.EndsWith(".zip") || path.EndsWith(".mp4") || path.EndsWith(".mp3");
        }

        private static IEnumerable<Uri> ExtractInternalLinks(IDocument doc, Uri baseUri)
        {
            foreach (var a in doc.QuerySelectorAll("a[href]"))
            {
                var href = a.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href) ||
                    href.StartsWith("#") ||
                    href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Uri.TryCreate(baseUri, href, out var uri))
                    yield return new UriBuilder(uri) { Fragment = "" }.Uri;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_stealthBrowser != null)
                await _stealthBrowser.DisposeAsync();
        }
    }
}