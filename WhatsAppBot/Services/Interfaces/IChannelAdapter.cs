using System.Threading.Tasks;

namespace WhatsAppBot.Services.Interfaces
{
    /// <summary>
    /// Abstraction for communication channels (WhatsApp, Telegram, etc.).
    /// Application services return channel-neutral responses; adapters translate to provider format.
    /// </summary>
    public interface IChannelAdapter
    {
        /// <summary>Channel identifier (e.g., "whatsapp", "telegram").</summary>
        string Channel { get; }

        /// <summary>Send a text message to a user.</summary>
        Task SendMessageAsync(string providerUserId, string message);

        /// <summary>Send an image message to a user.</summary>
        Task SendImageAsync(string providerUserId, string imageUrl, string? caption = null);
    }
}
