using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services
{
    public class SessionCleanupService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<SessionCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval;
        private readonly TimeSpan _sessionTimeout;

        public SessionCleanupService(
            IServiceProvider services,
            ILogger<SessionCleanupService> logger,
            IConfiguration config)
        {
            _services = services;
            _logger = logger;
            _cleanupInterval = TimeSpan.FromMinutes(config.GetValue<int>("Session:CleanupIntervalMinutes", 5));
            _sessionTimeout = TimeSpan.FromMinutes(config.GetValue<int>("Session:TimeoutMinutes", 30));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Session cleanup service started. Interval: {Interval} min, Timeout: {Timeout} min",
                _cleanupInterval.TotalMinutes,
                _sessionTimeout.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_cleanupInterval, stoppingToken);

                    using var scope = _services.CreateScope();
                    var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
                    sessionService.CleanupExpiredSessions(_sessionTimeout);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during session cleanup");
                }
            }

            _logger.LogInformation("Session cleanup service stopped");
        }
    }
}