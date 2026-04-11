using Microsoft.Playwright;
using Microsoft.Extensions.Logging;

namespace WhatsAppBot.Services.CaptchaSolver
{
    /// <summary>
    /// Stealth browser that evades bot detection.
    /// Mimics a real user's browser fingerprint.
    /// FREE - no API needed.
    /// </summary>
    public class StealthBrowser : IAsyncDisposable
    {
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private readonly ILogger? _logger;
        private bool _initialized;

        public StealthBrowser(ILogger? logger = null)
        {
            _logger = logger;
        }

        public async Task<IBrowser> GetBrowserAsync()
        {
            if (!_initialized)
                await InitializeAsync();

            return _browser!;
        }

        private async Task InitializeAsync()
        {
            _playwright = await Playwright.CreateAsync();

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-features=IsolateOrigins,site-per-process",
                    "--disable-infobars",
                    "--window-size=1920,1080",
                    "--start-maximized",
                    "--disable-extensions",
                    "--disable-gpu",
                    "--no-first-run"
                }
            });

            _initialized = true;
            _logger?.LogInformation("Stealth browser initialized");
        }

        public async Task<IBrowserContext> CreateStealthContextAsync()
        {
            var browser = await GetBrowserAsync();

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = GetRandomUserAgent(),
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "en-US",
                TimezoneId = "Africa/Lagos",
                Geolocation = new Geolocation { Latitude = 6.5244f, Longitude = 3.3792f },
                Permissions = new[] { "geolocation" },
                HasTouch = false,
                IsMobile = false,
                JavaScriptEnabled = true
            });

            // Inject stealth scripts to evade detection
            await context.AddInitScriptAsync(GetStealthScript());

            return context;
        }

        private static string GetStealthScript()
        {
            return @"
                // Remove webdriver flag
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                
                // Fake plugins
                Object.defineProperty(navigator, 'plugins', {
                    get: () => {
                        const plugins = [
                            { name: 'Chrome PDF Plugin', filename: 'internal-pdf-viewer' },
                            { name: 'Chrome PDF Viewer', filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai' },
                            { name: 'Native Client', filename: 'internal-nacl-plugin' }
                        ];
                        plugins.length = 3;
                        return plugins;
                    }
                });

                // Fake languages
                Object.defineProperty(navigator, 'languages', {
                    get: () => ['en-US', 'en', 'fr']
                });

                // Fake platform
                Object.defineProperty(navigator, 'platform', {
                    get: () => 'Win32'
                });

                // Chrome runtime
                window.chrome = {
                    runtime: {
                        id: undefined,
                        onMessage: { addListener: () => {} },
                        sendMessage: () => {}
                    },
                    loadTimes: () => ({
                        commitLoadTime: Date.now() / 1000,
                        connectionInfo: 'h2',
                        finishDocumentLoadTime: Date.now() / 1000 + 0.5,
                        finishLoadTime: Date.now() / 1000 + 1,
                        firstPaintAfterLoadTime: 0,
                        firstPaintTime: Date.now() / 1000 + 0.1,
                        navigationType: 'Other',
                        npnNegotiatedProtocol: 'h2',
                        requestTime: Date.now() / 1000 - 1,
                        startLoadTime: Date.now() / 1000 - 0.5,
                        wasAlternateProtocolAvailable: false,
                        wasFetchedViaSpdy: true,
                        wasNpnNegotiated: true
                    }),
                    csi: () => ({
                        onloadT: Date.now(),
                        pageT: Date.now() / 1000,
                        startE: Date.now() - 1000,
                        tran: 15
                    })
                };

                // WebGL vendor/renderer
                const getParameter = WebGLRenderingContext.prototype.getParameter;
                WebGLRenderingContext.prototype.getParameter = function(parameter) {
                    if (parameter === 37445) return 'Intel Inc.';
                    if (parameter === 37446) return 'Intel Iris OpenGL Engine';
                    return getParameter.call(this, parameter);
                };

                // Fake permissions
                const originalQuery = window.navigator.permissions.query;
                window.navigator.permissions.query = (parameters) => (
                    parameters.name === 'notifications' ?
                        Promise.resolve({ state: Notification.permission }) :
                        originalQuery(parameters)
                );

                // Canvas fingerprint noise
                const toBlob = HTMLCanvasElement.prototype.toBlob;
                const toDataURL = HTMLCanvasElement.prototype.toDataURL;
                const getImageData = CanvasRenderingContext2D.prototype.getImageData;

                HTMLCanvasElement.prototype.toBlob = function() {
                    const context = this.getContext('2d');
                    if (context) {
                        const imageData = context.getImageData(0, 0, this.width, this.height);
                        for (let i = 0; i < imageData.data.length; i += 4) {
                            imageData.data[i] += Math.floor(Math.random() * 2);
                        }
                        context.putImageData(imageData, 0, 0);
                    }
                    return toBlob.apply(this, arguments);
                };

                // Prevent detection of automation
                delete navigator.__proto__.webdriver;
            ";
        }

        private static string GetRandomUserAgent()
        {
            var userAgents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0",
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            };

            return userAgents[Random.Shared.Next(userAgents.Length)];
        }

        public async ValueTask DisposeAsync()
        {
            if (_browser != null)
            {
                await _browser.DisposeAsync();
                _browser = null;
            }

            _playwright?.Dispose();
            _playwright = null;
            _initialized = false;
        }
    }
}