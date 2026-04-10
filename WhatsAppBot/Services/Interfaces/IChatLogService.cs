using System.Threading.Tasks;

namespace WhatsAppBot.Services.Interfaces
{
    public interface IChatLogService
    {
        Task LogInboundAsync(string phoneNumber, string? name, string? email, string message, string messageType = "text");
        Task LogOutboundAsync(string phoneNumber, string? name, string? email, string message, string messageType = "text");
    }
}