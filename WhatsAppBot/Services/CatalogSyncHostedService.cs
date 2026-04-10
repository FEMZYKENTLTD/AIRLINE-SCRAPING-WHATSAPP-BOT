using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services
{
    /// <summary>
    /// Runs the catalog sync on an interval based on ScrapingOptions.
    /// Calls ICatalogSyncService which runs ALL scrapers + DB upserts.
    /// </summary>
    public class CatalogSyncHostedService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<CatalogSyncHostedService> _logger;
        private readonly ScrapingOptions _opt;

        public CatalogSyncHostedService(
            IServiceProvider services,
            IOptions<ScrapingOptions> options,
            ILogger<CatalogSyncHostedService> logger)
        {
            _services = services;
            _logger = logger;
            _opt = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_opt.Enabled)
            {
                _logger.LogInformation("CatalogSyncHostedService: Disabled via config (Scraping:Enabled=false).");
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, _opt.IntervalMinutes));

            _logger.LogInformation(
                "CatalogSyncHostedService started. Interval: {Minutes} min. Run-on-startup: {RunOnStartup}",
                interval.TotalMinutes, _opt.RunOnStartup);

            // ✅ immediate run when bot starts
            if (_opt.RunOnStartup)
                await SafeRunOnce(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken);
                    await SafeRunOnce(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("CatalogSyncHostedService stopped.");
        }

        private async Task SafeRunOnce(CancellationToken ct)
        {
            try
            {
                using var scope = _services.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<ICatalogSyncService>();

                await sync.RunOnceAsync(ct);

                _logger.LogInformation("Catalog sync run completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Catalog sync run failed.");
            }
        }
    }
}
