using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Database-backed session management.
    /// Sessions expire after 7 days of inactivity by default.
    /// </summary>
    public class PersistentSessionService : IPersistentSessionService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PersistentSessionService> _logger;
        private readonly TimeSpan _sessionTimeout;

        public PersistentSessionService(AppDbContext db, ILogger<PersistentSessionService> logger, IConfiguration config)
        {
            _db = db;
            _logger = logger;
            _sessionTimeout = TimeSpan.FromMinutes(config.GetValue<int>("Session:TimeoutMinutes", 10080)); // 7 days default
        }

        public async Task<AppSession> GetOrCreateSessionAsync(
            string channel, string providerUserId, int? userId = null, CancellationToken ct = default)
        {
            // Find active session
            var session = await _db.AppSessions
                .FirstOrDefaultAsync(s =>
                    s.Channel == channel &&
                    s.ConversationId == providerUserId &&
                    !s.IsExpired, ct);

            if (session != null)
            {
                session.Touch(_sessionTimeout);
                await _db.SaveChangesAsync(ct);

                _logger.LogDebug("Resumed session {SessionId} for {Channel}:{ProviderUserId}",
                    session.SessionId, channel, providerUserId);

                return session;
            }

            // Create new session
            session = new AppSession
            {
                Channel = channel,
                ConversationId = providerUserId,
                UserId = userId,
                CurrentState = "New",
                CreatedAtUtc = DateTime.UtcNow
            };
            session.Touch(_sessionTimeout);

            _db.AppSessions.Add(session);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Created new session {SessionId} for {Channel}:{ProviderUserId}",
                session.SessionId, channel, providerUserId);

            return session;
        }

        public async Task UpdateSessionAsync(AppSession session, CancellationToken ct = default)
        {
            session.Touch(_sessionTimeout);
            _db.AppSessions.Update(session);
            await _db.SaveChangesAsync(ct);

            _logger.LogDebug("Updated session {SessionId}, state: {State}",
                session.SessionId, session.CurrentState);
        }

        public async Task<int> CleanupExpiredSessionsAsync(CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow;
            var expired = await _db.AppSessions
                .Where(s => s.ExpiresAtUtc.HasValue && s.ExpiresAtUtc.Value < cutoff)
                .ToListAsync(ct);

            if (expired.Count == 0) return 0;

            // Don't delete, just mark as expired by setting state
            foreach (var session in expired)
            {
                session.CurrentState = "Expired";
                session.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Marked {Count} expired sessions", expired.Count);
            return expired.Count;
        }

        public async Task<AppSession?> GetBySessionIdAsync(string sessionId, CancellationToken ct = default)
        {
            return await _db.AppSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
        }

        public async Task<AppSession> TransitionStateAsync(
            string sessionId, string newState, string? workflowStep = null, CancellationToken ct = default)
        {
            var session = await GetBySessionIdAsync(sessionId, ct)
                ?? throw new InvalidOperationException($"Session {sessionId} not found");

            session.CurrentState = newState;
            session.WorkflowStep = workflowStep;
            session.Touch(_sessionTimeout);

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Session {SessionId} transitioned to {State}/{Step}",
                sessionId, newState, workflowStep ?? "none");

            return session;
        }

        public async Task<int> GetActiveCountAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            return await _db.AppSessions
                .CountAsync(s => s.ExpiresAtUtc == null || s.ExpiresAtUtc >= now, ct);
        }

        public async Task<AppSession> ResetAsync(AppSession session, CancellationToken ct = default)
        {
            session.CurrentState = "New";
            session.WorkflowStep = null;
            session.ContextData = null;
            session.Touch(_sessionTimeout);

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Session {SessionId} reset to fresh state", session.SessionId);
            return session;
        }
    }
}
