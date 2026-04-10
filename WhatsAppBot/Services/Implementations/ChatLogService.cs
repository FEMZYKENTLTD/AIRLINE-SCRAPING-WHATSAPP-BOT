using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Implementations
{
    public class ChatLogService : IChatLogService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ChatLogService> _logger;

        public ChatLogService(AppDbContext db, ILogger<ChatLogService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task LogInboundAsync(
            string phoneNumber,
            string? name,
            string? email,
            string message,
            string messageType = "text")
        {
            await LogAsync(phoneNumber, name, email, message, "inbound", messageType);
        }

        public async Task LogOutboundAsync(
            string phoneNumber,
            string? name,
            string? email,
            string message,
            string messageType = "text")
        {
            await LogAsync(phoneNumber, name, email, message, "outbound", messageType);
        }

        private async Task LogAsync(
            string phoneNumber,
            string? name,
            string? email,
            string message,
            string direction,
            string messageType)
        {
            try
            {
                var log = new ChatLog
                {
                    PhoneNumber = phoneNumber,
                    Name = name,
                    Email = email,
                    Message = message,
                    Direction = direction,
                    MessageType = messageType,
                    Timestamp = DateTime.UtcNow
                };

                _db.ChatLogs.Add(log);
                await _db.SaveChangesAsync();

                var truncatedMessage = message.Length > 80 ? message.Substring(0, 80) + "..." : message;
                _logger.LogInformation(
                    "[{Direction}] {Phone} | {Name} | {Email} | {Message}",
                    direction.ToUpper(),
                    phoneNumber,
                    name ?? "N/A",
                    email ?? "N/A",
                    truncatedMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log chat message");
            }
        }
    }
}