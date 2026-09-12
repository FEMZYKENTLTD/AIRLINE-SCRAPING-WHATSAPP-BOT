using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace WhatsAppBot.Services.CaptchaSolver
{
    /// <summary>
    /// Manages a Playwright browser instance configured for stealth scraping.
    /// Reduces detection by anti-bot systems through realistic browser fingerprints.
    /// </summary>
    public class StealthBrowser : IAsyncDisposable
    {
        private readonly ILogger? _logger;
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private bool _disposed;

        public StealthBrowser(ILogger? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates a new stealth browser context with anti-detection settings.
        /// </summary>
        public async Task<IBrowserContext> CreateStealthContextAsync()
        {
            if (_playwright == null)
            {
                _playwright = await Playwright.CreateAsync();
            }

            if (_browser == null)
            {
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--disable-features=IsolateOrigins,site-per-process",
                        "--no-sandbox"
                    }
                });

                _logger?.LogDebug("Stealth browser launched");
            }

            var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "en-US",
                TimezoneId = "America/New_York",
                JavaScriptEnabled = true
            });

            // Inject stealth scripts to override navigator.webdriver
            await context.AddInitScriptAsync(@"
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
                Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] });
                window.chrome = { runtime: {} };
            ");

            return context;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (_browser != null)
            {
                try { await _browser.CloseAsync(); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Error closing browser"); }
            }

            _playwright?.Dispose();
        }
    }
}
