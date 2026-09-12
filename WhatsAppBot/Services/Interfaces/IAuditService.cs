using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models;

namespace WhatsAppBot.Services.Interfaces
{
    /// <summary>
    /// Audit logging for security and administrative events.
    /// </summary>
    public interface IAuditService
    {
        /// <summary>Log an audit event.</summary>
        Task LogAsync(string action, string? entityType = null, string? entityId = null,
            string? actorId = null, string? channel = null, string? details = null,
            string? ipAddress = null, string? correlationId = null, CancellationToken ct = default);

        /// <summary>Get recent audit logs.</summary>
        Task<List<AuditLog>> ListAsync(int skip, int take, string? action = null,
            string? entityType = null, CancellationToken ct = default);

        /// <summary>Get audit logs for a specific entity.</summary>
        Task<List<AuditLog>> GetByEntityAsync(string entityType, string entityId,
            CancellationToken ct = default);
    }
}
