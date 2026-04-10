using System.Threading;
using System.Threading.Tasks;

namespace WhatsAppBot.Services.Interfaces
{
    /// <summary>
    /// Orchestrates scraping + upserting into DB.
    /// The HostedService calls this on a schedule.
    /// </summary>
    public interface ICatalogSyncService
    {
        Task RunOnceAsync(CancellationToken ct);
    }
}
