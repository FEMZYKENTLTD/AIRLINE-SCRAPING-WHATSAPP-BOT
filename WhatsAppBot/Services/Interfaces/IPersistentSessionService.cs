using System;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models;

namespace WhatsAppBot.Services.Interfaces
{
    /// <summary>
    /// Database-backed session management with 7-day inactivity expiration.
    /// </summary>
    public interface IPersistentSessionService
    {
        /// <summary>Get or create a session for the given channel and provider user ID.</summary>
        Task<AppSession> GetOrCreateSessionAsync(string channel, string providerUserId, int? userId = null, CancellationToken ct = default);

        /// <summary>Update session activity and state.</summary>
        Task UpdateSessionAsync(AppSession session, CancellationToken ct = default);

        /// <summary>Expire sessions that have exceeded the timeout period.</summary>
        Task<int> CleanupExpiredSessionsAsync(CancellationToken ct = default);

        /// <summary>Get session by session ID.</summary>
        Task<AppSession?> GetBySessionIdAsync(string sessionId, CancellationToken ct = default);

        /// <summary>Mark session with a new state/workflow step.</summary>
        Task<AppSession> TransitionStateAsync(string sessionId, string newState, string? workflowStep = null, CancellationToken ct = default);

        /// <summary>Get count of active sessions.</summary>
        Task<int> GetActiveCountAsync(CancellationToken ct = default);

        /// <summary>
        /// Reset a session to a fresh state (the /reset command): back to "New",
        /// workflow step and context data cleared, activity refreshed.
        /// The linked User record is untouched.
        /// </summary>
        Task<AppSession> ResetAsync(AppSession session, CancellationToken ct = default);
    }
}
