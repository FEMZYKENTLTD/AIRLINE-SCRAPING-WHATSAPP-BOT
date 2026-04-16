using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using WhatsAppBot.Services.Automation;

namespace WhatsAppBot.Services.Automation
{
    public class StealthBrowserManager
    {
        private readonly ProxyManager _proxy;

        public StealthBrowserManager(ProxyManager proxy)
        {
            _proxy = proxy;
        }

        public async Task<IBrowserContext> CreateContextAsync()
        {
            var playwright = await Playwright.CreateAsync();
            var proxyServer = _proxy.GetNextProxy();

            var launchOptions = new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-dev-shm-usage",
                    "--disable-gpu"
                }
            };

            if (!string.IsNullOrWhiteSpace(proxyServer))
            {
                launchOptions.Proxy = new Proxy { Server = proxyServer };
            }

            var browser = await playwright.Chromium.LaunchAsync(launchOptions);

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = GetRandomUserAgent(),
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "en-US",
                TimezoneId = "Africa/Lagos"
            });

            await context.AddInitScriptAsync(@"
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                Object.defineProperty(navigator, 'plugins', { get: () => [1,2,3,4,5] });
                Object.defineProperty(navigator, 'languages', { get: () => ['en-US','en'] });
                window.chrome = { runtime: {} };
            ");

            return context;
        }

        private static string GetRandomUserAgent()
        {
            var agents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0"
            };
            return agents[Random.Shared.Next(agents.Length)];
        }
    }
}