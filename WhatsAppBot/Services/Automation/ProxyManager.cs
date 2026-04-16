using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace WhatsAppBot.Services.Automation
{
    public class ProxyManager
    {
        private readonly List<string> _proxies = new();
        private int _currentIndex;
        private readonly ILogger<ProxyManager> _logger;

        public ProxyManager(ILogger<ProxyManager> logger)
        {
            _logger = logger;

            var env = Environment.GetEnvironmentVariable("PROXY_LIST");
            if (!string.IsNullOrWhiteSpace(env))
            {
                _proxies = env.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                _logger.LogInformation("ProxyManager loaded {Count} proxies", _proxies.Count);
            }
            else
            {
                _logger.LogWarning("No proxies configured. Set PROXY_LIST in .env for IP rotation.");
            }
        }

        public string? GetNextProxy()
        {
            if (_proxies.Count == 0) return null;

            var proxy = _proxies[_currentIndex];
            _currentIndex = (_currentIndex + 1) % _proxies.Count;
            _logger.LogDebug("Using proxy: {Proxy}", proxy);
            return proxy;
        }

        public bool HasProxies => _proxies.Count > 0;
        public int ProxyCount => _proxies.Count;
    }
}