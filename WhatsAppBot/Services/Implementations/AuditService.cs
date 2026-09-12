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
    public class AuditService : IAuditService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AuditService> _logger;

        public AuditService(AppDbContext db, ILogger<AuditService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task LogAsync(string action, string? entityType = null, string? entityId = null,
            string? actorId = null, string? channel = null, string? details = null,
            string? ipAddress = null, string? correlationId = null, CancellationToken ct = default)
        {
            try
            {
                var entry = new AuditLog
                {
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    ActorId = actorId,
                    Channel = channel,
                    Details = details,
                    IpAddress = ipAddress,
                    CorrelationId = correlationId,
                    Timestamp = DateTime.UtcNow
                };

                _db.AuditLogs.Add(entry);
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("AUDIT: {Action} | {Entity}:{EntityId} | Actor: {Actor}",
                    action, entityType ?? "-", entityId ?? "-", actorId ?? "system");
            }
            catch (Exception ex)
            {
                // Audit logging should never crash the application
                _logger.LogError(ex, "Failed to write audit log for action: {Action}", action);
            }
        }

        public async Task<List<AuditLog>> ListAsync(int skip, int take, string? action = null,
            string? entityType = null, CancellationToken ct = default)
        {
            var query = _db.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(action))
                query = query.Where(a => a.Action == action);
            if (!string.IsNullOrWhiteSpace(entityType))
                query = query.Where(a => a.EntityType == entityType);

            return await query
                .OrderByDescending(a => a.Timestamp)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);
        }

        public async Task<List<AuditLog>> GetByEntityAsync(string entityType, string entityId,
            CancellationToken ct = default)
        {
            return await _db.AuditLogs
                .Where(a => a.EntityType == entityType && a.EntityId == entityId)
                .OrderByDescending(a => a.Timestamp)
                .ToListAsync(ct);
        }
    }
}
