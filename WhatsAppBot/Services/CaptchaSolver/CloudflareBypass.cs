using Microsoft.Playwright;
using Microsoft.Extensions.Logging;

namespace WhatsAppBot.Services.CaptchaSolver
{
    /// <summary>
    /// Handles Cloudflare "Just a moment" and "Verify you are human" challenges.
    /// FREE - works by waiting for the challenge to auto-resolve in a real browser.
    /// </summary>
    public class CloudflareBypass
    {
        private readonly ILogger? _logger;

        public CloudflareBypass(ILogger? logger = null)
        {
            _logger = logger;
        }

        public async Task<bool> IsCloudflareChallenge(IPage page)
        {
            try
            {
                var title = await page.TitleAsync();
                var content = await page.ContentAsync();
                var contentLower = content.ToLowerInvariant();

                return title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
                       title.Contains("Attention Required", StringComparison.OrdinalIgnoreCase) ||
                       title.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase) ||
                       contentLower.Contains("cf-challenge-running") ||
                       contentLower.Contains("cf_chl_opt") ||
                       contentLower.Contains("turnstile") ||
                       (contentLower.Contains("cloudflare") && contentLower.Contains("ray id"));
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> WaitForResolutionAsync(IPage page, int maxWaitSeconds = 45, CancellationToken ct = default)
        {
            _logger?.LogInformation("Cloudflare challenge detected, waiting for auto-resolution...");

            for (int i = 0; i < maxWaitSeconds; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1000, ct);

                // Check if challenge is resolved
                if (!await IsCloudflareChallenge(page))
                {
                    _logger?.LogInformation("Cloudflare challenge resolved after {Seconds}s", i + 1);

                    // Wait a bit more for page to fully load
                    await Task.Delay(2000, ct);
                    return true;
                }

                // Try clicking the Turnstile checkbox if visible
                if (i == 5 || i == 15 || i == 25)
                {
                    await TryClickTurnstile(page);
                }
            }

            _logger?.LogWarning("Cloudflare challenge did NOT resolve within {Seconds}s", maxWaitSeconds);
            return false;
        }

        private async Task TryClickTurnstile(IPage page)
        {
            try
            {
                // Cloudflare Turnstile checkbox
                var selectors = new[]
                {
                    "iframe[src*='challenges.cloudflare.com']",
                    "#cf-turnstile-response",
                    ".cf-turnstile",
                    "input[name='cf-turnstile-response']"
                };

                foreach (var selector in selectors)
                {
                    var element = await page.QuerySelectorAsync(selector);
                    if (element != null)
                    {
                        // If it's an iframe, try to interact with it
                        if (selector.Contains("iframe"))
                        {
                            var frame = await element.ContentFrameAsync();
                            if (frame != null)
                            {
                                var checkbox = await frame.QuerySelectorAsync("input[type='checkbox']");
                                if (checkbox != null)
                                {
                                    await checkbox.ClickAsync();
                                    _logger?.LogInformation("Clicked Turnstile checkbox");
                                    await Task.Delay(3000);
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Turnstile click attempt failed (normal)");
            }
        }
    }
}