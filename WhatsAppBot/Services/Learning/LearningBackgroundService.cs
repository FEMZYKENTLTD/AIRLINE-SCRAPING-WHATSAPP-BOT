using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WhatsAppBot.Services.Learning
{
    /// <summary>
    /// Background service that periodically processes conversation insights
    /// and updates the knowledge base.
    /// </summary>
    public class LearningBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<LearningBackgroundService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromHours(6);

        public LearningBackgroundService(
            IServiceProvider services,
            ILogger<LearningBackgroundService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Learning background service started. Interval: {Interval}", _interval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, stoppingToken);

                    using var scope = _services.CreateScope();
                    var knowledge = scope.ServiceProvider.GetService<IKnowledgeService>();

                    if (knowledge != null)
                    {
                        _logger.LogDebug("Running periodic knowledge maintenance");
                        // Future: cleanup old insights, boost popular entries, etc.
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in learning background service");
                }
            }

            _logger.LogInformation("Learning background service stopped");
        }
    }
}
