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
    public class ServiceRequestService : IServiceRequestService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ServiceRequestService> _logger;

        public ServiceRequestService(AppDbContext db, ILogger<ServiceRequestService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ServiceRequest> CreateAsync(string requestType, string channel,
            int? userId = null, int? sessionId = null, string? summary = null,
            CancellationToken ct = default)
        {
            var request = new ServiceRequest
            {
                RequestCode = GenerateRequestCode(),
                RequestType = requestType,
                Channel = channel,
                UserId = userId,
                SessionId = sessionId,
                RequestSummary = summary,
                Status = ServiceRequestStatus.New,
                Priority = "Normal",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _db.ServiceRequests.Add(request);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Service request {Code} created: {Type} via {Channel}",
                request.RequestCode, requestType, channel);

            return request;
        }

        public async Task<ServiceRequest?> GetByCodeAsync(string requestCode, CancellationToken ct = default)
        {
            return await _db.ServiceRequests
                .Include(sr => sr.User)
                .FirstOrDefaultAsync(sr => sr.RequestCode == requestCode, ct);
        }

        public async Task<ServiceRequest?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _db.ServiceRequests
                .Include(sr => sr.User)
                .FirstOrDefaultAsync(sr => sr.Id == id, ct);
        }

        public async Task<ServiceRequest> UpdateStatusAsync(string requestCode, ServiceRequestStatus status,
            string? message = null, CancellationToken ct = default)
        {
            var request = await GetByCodeAsync(requestCode, ct)
                ?? throw new InvalidOperationException($"Service request {requestCode} not found");

            request.Status = status;
            request.StatusMessage = message;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Service request {Code} status changed to {Status}",
                requestCode, status);

            return request;
        }

        public async Task<ServiceRequest> EscalateAsync(string requestCode, string? reason = null,
            string? agentId = null, CancellationToken ct = default)
        {
            var request = await GetByCodeAsync(requestCode, ct)
                ?? throw new InvalidOperationException($"Service request {requestCode} not found");

            request.Status = ServiceRequestStatus.Escalated;
            request.RequiresHumanAgent = true;
            request.AssignedAgentId = agentId;
            request.StatusMessage = reason ?? "Escalated to human agent";
            request.EscalatedAtUtc = DateTime.UtcNow;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogWarning("Service request {Code} escalated: {Reason}",
                requestCode, reason ?? "No reason specified");

            return request;
        }

        public async Task<ServiceRequest> CompleteAsync(string requestCode, string? resultData = null,
            CancellationToken ct = default)
        {
            var request = await GetByCodeAsync(requestCode, ct)
                ?? throw new InvalidOperationException($"Service request {requestCode} not found");

            request.Status = ServiceRequestStatus.Completed;
            request.ResultData = resultData;
            request.CompletedAtUtc = DateTime.UtcNow;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Service request {Code} completed", requestCode);

            return request;
        }

        public async Task<List<ServiceRequest>> ListAsync(int skip, int take,
            ServiceRequestStatus? status = null, string? requestType = null,
            string? channel = null, CancellationToken ct = default)
        {
            var query = _db.ServiceRequests
                .Include(sr => sr.User)
                .AsQueryable();

            if (status.HasValue)
                query = query.Where(sr => sr.Status == status.Value);
            if (!string.IsNullOrWhiteSpace(requestType))
                query = query.Where(sr => sr.RequestType == requestType);
            if (!string.IsNullOrWhiteSpace(channel))
                query = query.Where(sr => sr.Channel == channel);

            return await query
                .OrderByDescending(sr => sr.CreatedAtUtc)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);
        }

        public async Task<List<ServiceRequest>> GetByUserIdAsync(int userId, int limit = 20,
            CancellationToken ct = default)
        {
            return await _db.ServiceRequests
                .Where(sr => sr.UserId == userId)
                .OrderByDescending(sr => sr.CreatedAtUtc)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task<Dictionary<ServiceRequestStatus, int>> GetStatusCountsAsync(CancellationToken ct = default)
        {
            return await _db.ServiceRequests
                .GroupBy(sr => sr.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Status, g => g.Count, ct);
        }

        public string GenerateRequestCode()
        {
            var timestamp = DateTime.UtcNow.ToString("yyMMdd");
            var random = new Random().Next(1000, 9999);
            return $"SR-{timestamp}-{random}";
        }
    }
}
