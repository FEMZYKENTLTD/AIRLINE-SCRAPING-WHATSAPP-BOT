using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace WhatsAppBot.Services.CaptchaSolver
{
    /// <summary>
    /// Master CAPTCHA solver that coordinates all solving methods.
    /// Tries FREE methods first, falls back to paid services only if needed.
    /// </summary>
    public class SelfCaptchaSolver
    {
        private readonly CloudflareBypass _cloudflare;
        private readonly RecaptchaAudioSolver _recaptcha;
        private readonly ImageCaptchaSolver _imageSolver;
        private readonly ILogger<SelfCaptchaSolver> _logger;

        public SelfCaptchaSolver(ILogger<SelfCaptchaSolver> logger)
        {
            _logger = logger;
            _cloudflare = new CloudflareBypass(logger);
            _recaptcha = new RecaptchaAudioSolver(logger);
            _imageSolver = new ImageCaptchaSolver(logger);
        }

        /// <summary>
        /// Detects what type of CAPTCHA/challenge is on the page and solves it.
        /// </summary>
        public async Task<CaptchaResult> SolvePageChallengeAsync(
            IPage page,
            CancellationToken ct = default)
        {
            var result = new CaptchaResult();

            try
            {
                var html = await page.ContentAsync();
                var htmlLower = html.ToLowerInvariant();

                // Layer 1: Cloudflare Challenge
                if (await _cloudflare.IsCloudflareChallenge(page))
                {
                    _logger.LogInformation("Detected: Cloudflare challenge");
                    result.Type = CaptchaType.Cloudflare;

                    var solved = await _cloudflare.WaitForResolutionAsync(page, 45, ct);
                    result.Solved = solved;
                    result.Method = "Cloudflare Auto-Wait (FREE)";

                    if (solved)
                    {
                        result.PageHtml = await page.ContentAsync();
                        return result;
                    }
                }

                // Layer 2: reCAPTCHA v2
                if (htmlLower.Contains("recaptcha") || htmlLower.Contains("g-recaptcha"))
                {
                    _logger.LogInformation("Detected: reCAPTCHA");
                    result.Type = CaptchaType.RecaptchaV2;

                    var solved = await _recaptcha.SolveRecaptchaOnPageAsync(page);
                    result.Solved = solved;
                    result.Method = "reCAPTCHA Audio Solver (FREE)";

                    if (solved)
                    {
                        await Task.Delay(2000, ct);
                        result.PageHtml = await page.ContentAsync();
                        return result;
                    }
                }

                // Layer 3: hCaptcha
                if (htmlLower.Contains("hcaptcha") || htmlLower.Contains("h-captcha"))
                {
                    _logger.LogInformation("Detected: hCaptcha - attempting stealth bypass");
                    result.Type = CaptchaType.HCaptcha;

                    // hCaptcha sometimes resolves with stealth browser alone
                    await Task.Delay(5000, ct);
                    var newHtml = await page.ContentAsync();

                    if (!newHtml.ToLowerInvariant().Contains("hcaptcha"))
                    {
                        result.Solved = true;
                        result.Method = "Stealth Browser Auto-Bypass (FREE)";
                        result.PageHtml = newHtml;
                        return result;
                    }

                    result.Solved = false;
                    result.Method = "hCaptcha not solvable without paid service";
                }

                // Layer 4: Simple Image CAPTCHA
                if (htmlLower.Contains("captcha_image") || htmlLower.Contains("captcha-image"))
                {
                    _logger.LogInformation("Detected: Image CAPTCHA");
                    result.Type = CaptchaType.Image;

                    // Try to find and solve image CAPTCHA
                    var imgElement = await page.QuerySelectorAsync("img[src*='captcha']");
                    if (imgElement != null)
                    {
                        var imgSrc = await imgElement.GetAttributeAsync("src");
                        if (!string.IsNullOrEmpty(imgSrc))
                        {
                            var solution = await _imageSolver.SolveImageCaptchaFromUrlAsync(imgSrc);
                            if (!string.IsNullOrEmpty(solution))
                            {
                                // Find input field and enter solution
                                var input = await page.QuerySelectorAsync("input[name*='captcha']") ??
                                            await page.QuerySelectorAsync("input[id*='captcha']");

                                if (input != null)
                                {
                                    await input.FillAsync(solution);

                                    // Find and click submit
                                    var submit = await page.QuerySelectorAsync("button[type='submit']") ??
                                                 await page.QuerySelectorAsync("input[type='submit']");
                                    if (submit != null)
                                    {
                                        await submit.ClickAsync();
                                        await Task.Delay(3000, ct);
                                    }

                                    result.Solved = true;
                                    result.Method = "Image OCR (Tesseract - FREE)";
                                    result.PageHtml = await page.ContentAsync();
                                    return result;
                                }
                            }
                        }
                    }
                }

                // No CAPTCHA detected
                result.Type = CaptchaType.None;
                result.Solved = true;
                result.Method = "No CAPTCHA";
                result.PageHtml = html;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CAPTCHA solving failed");
                result.Solved = false;
                result.Error = ex.Message;
            }

            return result;
        }
    }

    public class CaptchaResult
    {
        public CaptchaType Type { get; set; } = CaptchaType.None;
        public bool Solved { get; set; }
        public string Method { get; set; } = string.Empty;
        public string? PageHtml { get; set; }
        public string? Error { get; set; }
    }

    public enum CaptchaType
    {
        None,
        Cloudflare,
        RecaptchaV2,
        RecaptchaV3,
        HCaptcha,
        Image,
        Math,
        Unknown
    }
}