using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models;

namespace WhatsAppBot.Services.Interfaces
{
    /// <summary>
    /// Manages message persistence and conversation history.
    /// </summary>
    public interface IConversationService
    {
        /// <summary>Persist an incoming message.</summary>
        Task<Message> LogInboundAsync(string channel, string providerUserId, string content,
            string? providerMessageId = null, int? sessionId = null, int? userId = null,
            string messageType = "text", CancellationToken ct = default);

        /// <summary>Persist an outgoing message.</summary>
        Task<Message> LogOutboundAsync(string channel, string providerUserId, string content,
            int? sessionId = null, int? userId = null, string messageType = "text",
            CancellationToken ct = default);

        /// <summary>Check if a message with the given provider ID already exists (idempotency).</summary>
        Task<bool> ExistsByProviderMessageIdAsync(string providerMessageId, CancellationToken ct = default);

        /// <summary>Get conversation history for a session.</summary>
        Task<List<Message>> GetSessionMessagesAsync(int sessionId, int limit = 50, CancellationToken ct = default);

        /// <summary>Get recent messages for a user.</summary>
        Task<List<Message>> GetUserMessagesAsync(int userId, int limit = 50, CancellationToken ct = default);

        /// <summary>Get total message count.</summary>
        Task<int> GetCountAsync(CancellationToken ct = default);
    }
}
