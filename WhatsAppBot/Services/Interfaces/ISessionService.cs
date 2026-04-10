using System;
using WhatsAppBot.Models;

namespace WhatsAppBot.Services.Interfaces
{
    public interface ISessionService
    {
        UserSession GetOrCreateSession(string phoneNumber);
        void UpdateSession(UserSession session);
        void RemoveSession(string phoneNumber);
        void CleanupExpiredSessions(TimeSpan maxInactivity);
    }
}