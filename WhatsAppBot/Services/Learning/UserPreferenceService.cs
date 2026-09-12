using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppBot.Models.Learning;

namespace WhatsAppBot.Services.Learning
{
    /// <summary>
    /// Service for managing user preferences.
    /// Delegates to IKnowledgeService which already implements preference storage.
    /// </summary>
    public class UserPreferenceService
    {
        private readonly IKnowledgeService _knowledge;

        public UserPreferenceService(IKnowledgeService knowledge)
        {
            _knowledge = knowledge;
        }

        public Task<UserPreference?> GetAsync(string phoneNumber, string key)
            => _knowledge.GetPreferenceAsync(phoneNumber, key);

        public Task SetAsync(string phoneNumber, string key, string value)
            => _knowledge.SetPreferenceAsync(phoneNumber, key, value);

        public Task<List<UserPreference>> GetAllAsync(string phoneNumber)
            => _knowledge.GetAllPreferencesAsync(phoneNumber);
    }
}
