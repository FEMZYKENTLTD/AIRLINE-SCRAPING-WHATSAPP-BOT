using System.Threading.Tasks;

namespace WhatsAppBot.Services.Interfaces
{
    public interface IWhatsAppService
    {
        Task SendMessageAsync(string to, string message);

        // NOTE:
        // Meta WhatsApp Cloud API supports sending image via a public HTTPS link.
        // (Image must be reachable publicly, not localhost.)
        Task SendImageAsync(string to, string imageUrl, string? caption = null);
    }
}
