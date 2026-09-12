using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models;

namespace WhatsAppBot.Services.Interfaces
{
    /// <summary>
    /// Manages the lifecycle of service requests.
    /// </summary>
    public interface IServiceRequestService
    {
        /// <summary>Create a new service request.</summary>
        Task<ServiceRequest> CreateAsync(string requestType, string channel,
            int? userId = null, int? sessionId = null, string? summary = null,
            CancellationToken ct = default);

        /// <summary>Get by request code.</summary>
        Task<ServiceRequest?> GetByCodeAsync(string requestCode, CancellationToken ct = default);

        /// <summary>Get by ID.</summary>
        Task<ServiceRequest?> GetByIdAsync(int id, CancellationToken ct = default);

        /// <summary>Update the status of a service request.</summary>
        Task<ServiceRequest> UpdateStatusAsync(string requestCode, ServiceRequestStatus status,
            string? message = null, CancellationToken ct = default);

        /// <summary>Escalate to human agent.</summary>
        Task<ServiceRequest> EscalateAsync(string requestCode, string? reason = null,
            string? agentId = null, CancellationToken ct = default);

        /// <summary>Complete a service request.</summary>
        Task<ServiceRequest> CompleteAsync(string requestCode, string? resultData = null,
            CancellationToken ct = default);

        /// <summary>Get recent service requests with pagination.</summary>
        Task<List<ServiceRequest>> ListAsync(int skip, int take,
            ServiceRequestStatus? status = null, string? requestType = null,
            string? channel = null, CancellationToken ct = default);

        /// <summary>Get service requests for a user.</summary>
        Task<List<ServiceRequest>> GetByUserIdAsync(int userId, int limit = 20,
            CancellationToken ct = default);

        /// <summary>Get count by status.</summary>
        Task<Dictionary<ServiceRequestStatus, int>> GetStatusCountsAsync(CancellationToken ct = default);

        /// <summary>Generate a unique request code.</summary>
        string GenerateRequestCode();
    }
}
