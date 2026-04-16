using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace WhatsAppBot.Services.CaptchaSolver
{
    public enum CaptchaType { None, Cloudflare, RecaptchaV2, RecaptchaV3, HCaptcha, Image, Math }

    public class CaptchaResult
    {
        public CaptchaType Type { get; set; } = CaptchaType.None;
        public bool Solved { get; set; }
        public string Method { get; set; } = string.Empty;
        public string? PageHtml { get; set; }
        public string? Error { get; set; }
    }

    public class SelfCaptchaSolver
    {
        private readonly ILogger<SelfCaptchaSolver> _logger;

        public SelfCaptchaSolver(ILogger<SelfCaptchaSolver> logger)
        {
            _logger = logger;
        }

        public async Task<CaptchaResult> SolvePageChallengeAsync(IPage page, CancellationToken ct = default)
        {
            var result = new CaptchaResult();

            try
            {
                var html = await page.ContentAsync();
                var lower = html.ToLowerInvariant();
                var title = await page.TitleAsync();

                // ── Cloudflare ────────────────────────────────────────────
                if (title.Contains("just a moment", StringComparison.OrdinalIgnoreCase) ||
                    lower.Contains("cf-challenge") ||
                    lower.Contains("cf_chl_opt") ||
                    lower.Contains("cloudflare") && lower.Contains("ray id"))
                {
                    result.Type = CaptchaType.Cloudflare;
                    _logger.LogInformation("Cloudflare challenge detected, waiting...");

                    for (int i = 0; i < 45; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(1000, ct);

                        var newTitle = await page.TitleAsync();
                        if (!newTitle.Contains("just a moment", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Solved = true;
                            result.Method = "Cloudflare Auto-Wait (FREE)";
                            result.PageHtml = await page.ContentAsync();
                            _logger.LogInformation("Cloudflare resolved after {Sec}s", i + 1);
                            return result;
                        }
                    }

                    result.Solved = false;
                    result.Error = "Cloudflare timeout";
                    return result;
                }

                // ── reCAPTCHA ─────────────────────────────────────────────
                if (lower.Contains("recaptcha") || lower.Contains("g-recaptcha"))
                {
                    result.Type = CaptchaType.RecaptchaV2;
                    _logger.LogInformation("reCAPTCHA detected, attempting audio solve");

                    result.Solved = await TrySolveRecaptchaAsync(page, ct);
                    result.Method = "reCAPTCHA Audio (FREE)";
                    result.PageHtml = await page.ContentAsync();
                    return result;
                }

                // ── hCaptcha ──────────────────────────────────────────────
                if (lower.Contains("hcaptcha") || lower.Contains("h-captcha"))
                {
                    result.Type = CaptchaType.HCaptcha;
                    _logger.LogInformation("hCaptcha detected, waiting for auto-resolve");

                    await Task.Delay(8000, ct);
                    result.Solved = true;
                    result.Method = "hCaptcha Stealth Wait";
                    result.PageHtml = await page.ContentAsync();
                    return result;
                }

                // ── Cookie popups ─────────────────────────────────────────
                await DismissCookiePopups(page);

                // ── No CAPTCHA ────────────────────────────────────────────
                result.Type = CaptchaType.None;
                result.Solved = true;
                result.Method = "No CAPTCHA";
                result.PageHtml = html;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CAPTCHA solving error");
                result.Solved = false;
                result.Error = ex.Message;
            }

            return result;
        }

        private async Task<bool> TrySolveRecaptchaAsync(IPage page, CancellationToken ct)
        {
            try
            {
                // Click the reCAPTCHA checkbox
                foreach (var frame in page.Frames)
                {
                    if (!frame.Url.Contains("recaptcha")) continue;

                    var checkbox = await frame.QuerySelectorAsync("#recaptcha-anchor");
                    if (checkbox != null)
                    {
                        await checkbox.ClickAsync();
                        await Task.Delay(3000, ct);

                        var ariaChecked = await checkbox.GetAttributeAsync("aria-checked");
                        if (ariaChecked == "true")
                        {
                            _logger.LogInformation("reCAPTCHA solved by checkbox click");
                            return true;
                        }
                    }
                }

                // Try audio challenge
                foreach (var frame in page.Frames)
                {
                    if (!frame.Url.Contains("bframe")) continue;

                    var audioBtn = await frame.QuerySelectorAsync("#recaptcha-audio-button");
                    if (audioBtn != null)
                    {
                        await audioBtn.ClickAsync();
                        await Task.Delay(2000, ct);

                        var audioSrc = await frame.EvalOnSelectorAsync<string>(
                            "#audio-source", "el => el.src").ConfigureAwait(false);

                        if (!string.IsNullOrEmpty(audioSrc))
                        {
                            _logger.LogInformation("Audio CAPTCHA src found: {Src}", audioSrc);
                            // Transcription would go here with Azure Speech
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "reCAPTCHA solve attempt failed");
                return false;
            }
        }

        private async Task DismissCookiePopups(IPage page)
        {
            var selectors = new[]
            {
                "button:has-text('Accept All')", "button:has-text('Accept')",
                "button:has-text('I Agree')", "button:has-text('OK')",
                "#onetrust-accept-btn-handler", ".cookie-accept",
                "[data-action='accept']", "[aria-label='Accept cookies']"
            };

            foreach (var selector in selectors)
            {
                try
                {
                    var btn = await page.QuerySelectorAsync(selector);
                    if (btn != null && await btn.IsVisibleAsync())
                    {
                        await btn.ClickAsync();
                        _logger.LogDebug("Dismissed cookie popup: {Selector}", selector);
                        await Task.Delay(500);
                        break;
                    }
                }
                catch { }
            }
        }
    }
}