using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Implementations
{
    public class ConversationService : IConversationService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ConversationService> _logger;

        public ConversationService(AppDbContext db, ILogger<ConversationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Message> LogInboundAsync(
            string channel, string providerUserId, string content,
            string? providerMessageId = null, int? sessionId = null, int? userId = null,
            string messageType = "text", CancellationToken ct = default)
        {
            // Idempotency check
            if (!string.IsNullOrWhiteSpace(providerMessageId))
            {
                var exists = await ExistsByProviderMessageIdAsync(providerMessageId, ct);
                if (exists)
                {
                    _logger.LogDebug("Duplicate message detected: {ProviderMessageId}", providerMessageId);
                    return (await _db.Messages.FirstAsync(m => m.ProviderMessageId == providerMessageId, ct))!;
                }
            }

            var message = new Message
            {
                Channel = channel,
                ProviderUserId = providerUserId,
                ProviderMessageId = providerMessageId,
                Direction = "inbound",
                MessageType = messageType,
                Content = content,
                SessionId = sessionId,
                UserId = userId,
                ProcessingStatus = "received",
                Timestamp = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("[INBOUND] {Channel}:{ProviderUserId} | {Preview}",
                channel, providerUserId,
                content.Length > 80 ? content[..80] + "..." : content);

            return message;
        }

        public async Task<Message> LogOutboundAsync(
            string channel, string providerUserId, string content,
            int? sessionId = null, int? userId = null, string messageType = "text",
            CancellationToken ct = default)
        {
            var message = new Message
            {
                Channel = channel,
                ProviderUserId = providerUserId,
                Direction = "outbound",
                MessageType = messageType,
                Content = content,
                SessionId = sessionId,
                UserId = userId,
                ProcessingStatus = "sent",
                Timestamp = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("[OUTBOUND] {Channel}:{ProviderUserId} | {Preview}",
                channel, providerUserId,
                content.Length > 80 ? content[..80] + "..." : content);

            return message;
        }

        public async Task<bool> ExistsByProviderMessageIdAsync(string providerMessageId, CancellationToken ct = default)
        {
            return await _db.Messages.AnyAsync(m => m.ProviderMessageId == providerMessageId, ct);
        }

        public async Task<List<Message>> GetSessionMessagesAsync(int sessionId, int limit = 50, CancellationToken ct = default)
        {
            return await _db.Messages
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task<List<Message>> GetUserMessagesAsync(int userId, int limit = 50, CancellationToken ct = default)
        {
            return await _db.Messages
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task<int> GetCountAsync(CancellationToken ct = default)
        {
            return await _db.Messages.CountAsync(ct);
        }
    }
}
