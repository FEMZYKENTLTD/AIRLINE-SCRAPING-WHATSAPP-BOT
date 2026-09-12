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
    public class UserService : IUserService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<UserService> _logger;

        public UserService(AppDbContext db, ILogger<UserService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<(User User, ChannelIdentity Identity)> FindOrCreateByChannelAsync(
            string channel, string providerUserId, string? displayName, CancellationToken ct = default)
        {
            // Try to find existing channel identity
            var identity = await _db.ChannelIdentities
                .Include(ci => ci.User)
                .FirstOrDefaultAsync(ci => ci.Channel == channel && ci.ProviderUserId == providerUserId, ct);

            if (identity != null)
            {
                identity.LastSeenAtUtc = DateTime.UtcNow;
                if (displayName != null)
                    identity.DisplayName = displayName;

                identity.User.LastActivityAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                _logger.LogDebug("Found existing user {UserId} via {Channel}:{ProviderUserId}",
                    identity.UserId, channel, providerUserId);

                return (identity.User, identity);
            }

            // Create new user and channel identity
            var user = new User
            {
                DisplayName = displayName,
                Status = "Active",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                LastActivityAtUtc = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct); // Save to get user.Id

            identity = new ChannelIdentity
            {
                UserId = user.Id,
                Channel = channel,
                ProviderUserId = providerUserId,
                DisplayName = displayName,
                CreatedAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            };

            _db.ChannelIdentities.Add(identity);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Created new user {UserId} via {Channel}:{ProviderUserId}",
                user.Id, channel, providerUserId);

            return (user, identity);
        }

        public async Task<User?> GetByIdAsync(int userId, CancellationToken ct = default)
        {
            return await _db.Users
                .Include(u => u.ChannelIdentities)
                .FirstOrDefaultAsync(u => u.Id == userId, ct);
        }

        public async Task<User?> GetByExternalIdAsync(string externalId, CancellationToken ct = default)
        {
            return await _db.Users
                .Include(u => u.ChannelIdentities)
                .FirstOrDefaultAsync(u => u.ExternalId == externalId, ct);
        }

        public async Task<User> UpdateProfileAsync(int userId, string? displayName, string? email, CancellationToken ct = default)
        {
            var user = await _db.Users.FindAsync(new object[] { userId }, ct)
                ?? throw new InvalidOperationException($"User {userId} not found");

            if (displayName != null) user.DisplayName = displayName;
            if (email != null) user.Email = email;
            user.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return user;
        }

        public async Task<List<User>> ListAsync(int skip, int take, string? search = null, CancellationToken ct = default)
        {
            var query = _db.Users
                .Include(u => u.ChannelIdentities)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                query = query.Where(u =>
                    (u.DisplayName != null && u.DisplayName.ToLower().Contains(term)) ||
                    (u.Email != null && u.Email.ToLower().Contains(term)) ||
                    (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
            }

            return await query
                .OrderByDescending(u => u.LastActivityAtUtc)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);
        }

        public async Task<int> GetCountAsync(CancellationToken ct = default)
        {
            return await _db.Users.CountAsync(ct);
        }

        public async Task TouchActivityAsync(int userId, CancellationToken ct = default)
        {
            var user = await _db.Users.FindAsync(new object[] { userId }, ct);
            if (user != null)
            {
                user.LastActivityAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }
    }
}
