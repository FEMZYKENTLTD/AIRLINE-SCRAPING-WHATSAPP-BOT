using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models.Learning;

namespace WhatsAppBot.Services.Learning
{
    public interface IKnowledgeService
    {
        Task<string?> FindAnswerAsync(string question, string? airlineKey = null);
        Task AddKnowledgeAsync(string category, string question, string answer,
            string source, string? airlineKey = null, string? sourceUrl = null);
        Task LearnFromConversationAsync(string phoneNumber,
            string userMessage, string botResponse, CancellationToken ct);
        Task<UserPreference?> GetPreferenceAsync(string phoneNumber, string key);
        Task SetPreferenceAsync(string phoneNumber, string key, string value);
        Task<List<UserPreference>> GetAllPreferencesAsync(string phoneNumber);
    }
}