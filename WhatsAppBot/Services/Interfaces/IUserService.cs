using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models;

namespace WhatsAppBot.Services.Interfaces
{
    /// <summary>
    /// Manages internal users and their channel identities.
    /// </summary>
    public interface IUserService
    {
        /// <summary>Find or create a user by channel identity.</summary>
        Task<(User User, ChannelIdentity Identity)> FindOrCreateByChannelAsync(
            string channel, string providerUserId, string? displayName, CancellationToken ct = default);

        /// <summary>Get user by internal ID.</summary>
        Task<User?> GetByIdAsync(int userId, CancellationToken ct = default);

        /// <summary>Get user by external ID.</summary>
        Task<User?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);

        /// <summary>Update user profile information.</summary>
        Task<User> UpdateProfileAsync(int userId, string? displayName, string? email, CancellationToken ct = default);

        /// <summary>List users with pagination.</summary>
        Task<List<User>> ListAsync(int skip, int take, string? search = null, CancellationToken ct = default);

        /// <summary>Get total user count.</summary>
        Task<int> GetCountAsync(CancellationToken ct = default);

        /// <summary>Update last activity timestamp.</summary>
        Task TouchActivityAsync(int userId, CancellationToken ct = default);
    }
}
