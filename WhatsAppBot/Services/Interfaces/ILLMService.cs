using System.Threading.Tasks;
using WhatsAppBot.Models;

namespace WhatsAppBot.Services.Interfaces
{
    public interface ILLMService
    {
        // NOTE:
        // Webhook payload parsers can occasionally yield null text (or empty).
        // Accepting nullable here prevents "possible null reference" warnings
        // and keeps the service resilient in production.
        Task<string> GetResponseAsync(UserSession session, string? userMessage);
    }
}
