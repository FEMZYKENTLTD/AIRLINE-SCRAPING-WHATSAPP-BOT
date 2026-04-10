using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Implementations
{
    public class InMemorySessionService : ISessionService
    {
        private readonly ConcurrentDictionary<string, UserSession> _sessions = new ConcurrentDictionary<string, UserSession>();
        private readonly IMemoryCache _cache;
        private readonly ILogger<InMemorySessionService> _logger;
        private readonly TimeSpan _sessionTimeout;

        public InMemorySessionService(
            IMemoryCache cache,
            ILogger<InMemorySessionService> logger,
            IConfiguration config)
        {
            _cache = cache;
            _logger = logger;
            _sessionTimeout = TimeSpan.FromMinutes(config.GetValue<int>("Session:TimeoutMinutes", 30));
        }

        public UserSession GetOrCreateSession(string phoneNumber)
        {
            var session = _sessions.GetOrAdd(phoneNumber, phone =>
            {
                _logger.LogInformation("Creating new session for {Phone}", phone);
                return new UserSession
                {
                    PhoneNumber = phone,
                    State = UserState.New
                };
            });

            session.UpdateActivity();

            var options = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(_sessionTimeout)
                .RegisterPostEvictionCallback((key, value, reason, state) =>
                {
                    if (reason == EvictionReason.Expired)
                    {
                        var phone = key.ToString()?.Replace("session_", "") ?? "";
                        _sessions.TryRemove(phone, out _);
                        _logger.LogInformation("Session expired and removed for {Phone}", phone);
                    }
                });

            _cache.Set($"session_{phoneNumber}", session, options);

            return session;
        }

        public void UpdateSession(UserSession session)
        {
            session.UpdateActivity();
            _sessions[session.PhoneNumber] = session;

            _logger.LogDebug("Updated session for {Phone}, State: {State}, Name: {Name}",
                session.PhoneNumber, session.State, session.Name);
        }

        public void RemoveSession(string phoneNumber)
        {
            _sessions.TryRemove(phoneNumber, out _);
            _cache.Remove($"session_{phoneNumber}");
            _logger.LogInformation("Removed session for {Phone}", phoneNumber);
        }

        public void CleanupExpiredSessions(TimeSpan maxInactivity)
        {
            var cutoff = DateTime.UtcNow - maxInactivity;
            var expiredSessions = _sessions
                .Where(kvp => kvp.Value.LastActivity < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var phone in expiredSessions)
            {
                RemoveSession(phone);
            }

            if (expiredSessions.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} expired sessions", expiredSessions.Count);
            }
        }
    }
}