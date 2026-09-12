using System.Threading;
using System.Threading.Tasks;

namespace WhatsAppBot.Services.Interfaces
{
    /// <summary>
    /// Service for sending messages through Telegram Bot API.
    /// </summary>
    public interface ITelegramService
    {
        /// <summary>Send a text message to a Telegram chat.</summary>
        Task SendMessageAsync(string chatId, string message, CancellationToken ct = default);

        /// <summary>Send a photo to a Telegram chat.</summary>
        Task SendPhotoAsync(string chatId, string photoUrl, string? caption = null, CancellationToken ct = default);

        /// <summary>Set the bot's webhook URL.</summary>
        Task<bool> SetWebhookAsync(string webhookUrl, string? secretToken = null, CancellationToken ct = default);

        /// <summary>Delete the bot's webhook.</summary>
        Task<bool> DeleteWebhookAsync(CancellationToken ct = default);

        /// <summary>Get basic bot info.</summary>
        Task<string?> GetBotInfoAsync(CancellationToken ct = default);
    }
}
