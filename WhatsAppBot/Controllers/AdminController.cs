using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using WhatsAppBot.Models.Reservations;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Controllers
{
    /// <summary>
    /// Administrative API for managing users, sessions, messages, service requests, and system health.
    /// Requires authentication via JWT token.
    /// </summary>
    [ApiController]
    [Route("api/admin")]
    [Authorize]
    public class AdminController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IPersistentSessionService _sessionService;
        private readonly IConversationService _conversationService;
        private readonly IServiceRequestService _serviceRequestService;
        private readonly IAuditService _auditService;
        private readonly AppDbContext _db;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IUserService userService,
            IPersistentSessionService sessionService,
            IConversationService conversationService,
            IServiceRequestService serviceRequestService,
            IAuditService auditService,
            AppDbContext db,
            ILogger<AdminController> logger)
        {
            _userService = userService;
            _sessionService = sessionService;
            _conversationService = conversationService;
            _serviceRequestService = serviceRequestService;
            _auditService = auditService;
            _db = db;
            _logger = logger;
        }

        // ═══════════════════════════════════════════════════════════════════
        // DASHBOARD / OVERVIEW
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>Get system dashboard overview.</summary>
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard(CancellationToken ct)
        {
            var userCount = await _userService.GetCountAsync(ct);
            var activeSessions = await _sessionService.GetActiveCountAsync(ct);
            var messageCount = await _conversationService.GetCountAsync(ct);
            var requestCounts = await _serviceRequestService.GetStatusCountsAsync(ct);
            var reservationCount = await _db.Reservations.CountAsync(ct);
            var paymentCount = await _db.Payments.CountAsync(ct);

            return Ok(new
            {
                users = new { total = userCount },
                sessions = new { active = activeSessions },
                messages = new { total = messageCount },
                serviceRequests = requestCounts,
                reservations = new { total = reservationCount },
                payments = new { total = paymentCount },
                timestamp = DateTime.UtcNow
            });
        }

        // ═══════════════════════════════════════════════════════════════════
        // USERS
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>List users with optional search and pagination.</summary>
        [HttpGet("users")]
        public async Task<IActionResult> ListUsers(
            [FromQuery] int skip = 0, [FromQuery] int take = 20,
            [FromQuery] string? search = null, CancellationToken ct = default)
        {
            take = Math.Min(take, 100);
            var users = await _userService.ListAsync(skip, take, search, ct);
            var total = await _userService.GetCountAsync(ct);

            return Ok(new
            {
                items = users.Select(u => new
                {
                    id = u.Id,
                    externalId = u.ExternalId,
                    displayName = u.DisplayName,
                    email = u.Email,
                    emailVerified = u.EmailVerified,
                    phoneNumber = u.PhoneNumber,
                    status = u.Status,
                    channels = u.ChannelIdentities.Select(ci => new
                    {
                        channel = ci.Channel,
                        providerUserId = ci.ProviderUserId,
                        displayName = ci.DisplayName,
                        lastSeen = ci.LastSeenAtUtc
                    }),
                    createdAt = u.CreatedAtUtc,
                    lastActivity = u.LastActivityAtUtc
                }),
                total,
                skip,
                take
            });
        }

        /// <summary>Get user by ID.</summary>
        [HttpGet("users/{id:int}")]
        public async Task<IActionResult> GetUser(int id, CancellationToken ct)
        {
            var user = await _userService.GetByIdAsync(id, ct);
            if (user == null) return NotFound();

            return Ok(new
            {
                id = user.Id,
                externalId = user.ExternalId,
                displayName = user.DisplayName,
                email = user.Email,
                emailVerified = user.EmailVerified,
                phoneNumber = user.PhoneNumber,
                status = user.Status,
                channels = user.ChannelIdentities.Select(ci => new
                {
                    channel = ci.Channel,
                    providerUserId = ci.ProviderUserId,
                    displayName = ci.DisplayName,
                    createdAt = ci.CreatedAtUtc,
                    lastSeen = ci.LastSeenAtUtc
                }),
                createdAt = user.CreatedAtUtc,
                updatedAt = user.UpdatedAtUtc,
                lastActivity = user.LastActivityAtUtc,
                metadata = user.Metadata
            });
        }

        // ═══════════════════════════════════════════════════════════════════
        // SESSIONS
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>List active sessions.</summary>
        [HttpGet("sessions")]
        public async Task<IActionResult> ListSessions(
            [FromQuery] int skip = 0, [FromQuery] int take = 20,
            [FromQuery] string? channel = null, CancellationToken ct = default)
        {
            take = Math.Min(take, 100);
            var query = _db.AppSessions.AsQueryable();

            if (!string.IsNullOrWhiteSpace(channel))
                query = query.Where(s => s.Channel == channel);

            var sessions = await query
                .OrderByDescending(s => s.LastActivityAtUtc)
                .Skip(skip)
                .Take(take)
                .Select(s => new
                {
                    sessionId = s.SessionId,
                    channel = s.Channel,
                    conversationId = s.ConversationId,
                    userId = s.UserId,
                    currentState = s.CurrentState,
                    workflowStep = s.WorkflowStep,
                    createdAt = s.CreatedAtUtc,
                    lastActivity = s.LastActivityAtUtc,
                    expiresAt = s.ExpiresAtUtc,
                    isExpired = s.IsExpired
                })
                .ToListAsync(ct);

            var total = await _db.AppSessions.CountAsync(ct);

            return Ok(new { items = sessions, total, skip, take });
        }

        // ═══════════════════════════════════════════════════════════════════
        // MESSAGES
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>List recent messages.</summary>
        [HttpGet("messages")]
        public async Task<IActionResult> ListMessages(
            [FromQuery] int skip = 0, [FromQuery] int take = 50,
            [FromQuery] string? channel = null,
            [FromQuery] string? direction = null, CancellationToken ct = default)
        {
            take = Math.Min(take, 200);
            var query = _db.Messages.AsQueryable();

            if (!string.IsNullOrWhiteSpace(channel))
                query = query.Where(m => m.Channel == channel);
            if (!string.IsNullOrWhiteSpace(direction))
                query = query.Where(m => m.Direction == direction);

            var messages = await query
                .OrderByDescending(m => m.Timestamp)
                .Skip(skip)
                .Take(take)
                .Select(m => new
                {
                    id = m.Id,
                    channel = m.Channel,
                    direction = m.Direction,
                    messageType = m.MessageType,
                    content = m.Content != null && m.Content.Length > 200
                        ? m.Content[..200] + "..." : m.Content,
                    providerUserId = m.ProviderUserId,
                    providerMessageId = m.ProviderMessageId,
                    processingStatus = m.ProcessingStatus,
                    timestamp = m.Timestamp
                })
                .ToListAsync(ct);

            var total = await _db.Messages.CountAsync(ct);

            return Ok(new { items = messages, total, skip, take });
        }

        // ═══════════════════════════════════════════════════════════════════
        // SERVICE REQUESTS
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>List service requests with optional filters.</summary>
        [HttpGet("service-requests")]
        public async Task<IActionResult> ListServiceRequests(
            [FromQuery] int skip = 0, [FromQuery] int take = 20,
            [FromQuery] ServiceRequestStatus? status = null,
            [FromQuery] string? requestType = null,
            [FromQuery] string? channel = null, CancellationToken ct = default)
        {
            take = Math.Min(take, 100);
            var requests = await _serviceRequestService.ListAsync(
                skip, take, status, requestType, channel, ct);

            return Ok(new
            {
                items = requests.Select(r => new
                {
                    id = r.Id,
                    requestCode = r.RequestCode,
                    requestType = r.RequestType,
                    status = r.Status.ToString(),
                    channel = r.Channel,
                    userId = r.UserId,
                    userName = r.User?.DisplayName,
                    summary = r.RequestSummary,
                    requiresHuman = r.RequiresHumanAgent,
                    assignedAgent = r.AssignedAgentId,
                    priority = r.Priority,
                    reservationCode = r.ReservationCode,
                    createdAt = r.CreatedAtUtc,
                    updatedAt = r.UpdatedAtUtc,
                    completedAt = r.CompletedAtUtc,
                    escalatedAt = r.EscalatedAtUtc
                }),
                total = requests.Count,
                skip,
                take
            });
        }

        /// <summary>Update service request status.</summary>
        [HttpPut("service-requests/{requestCode}/status")]
        public async Task<IActionResult> UpdateServiceRequestStatus(
            string requestCode, [FromBody] UpdateStatusRequest request, CancellationToken ct)
        {
            try
            {
                var sr = await _serviceRequestService.UpdateStatusAsync(
                    requestCode, request.Status, request.Message, ct);

                await _auditService.LogAsync("ServiceRequestStatusChange",
                    "ServiceRequest", requestCode, User.Identity?.Name,
                    details: $"New status: {request.Status}");

                return Ok(new { requestCode = sr.RequestCode, status = sr.Status.ToString() });
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
        }

        /// <summary>Escalate a service request.</summary>
        [HttpPost("service-requests/{requestCode}/escalate")]
        public async Task<IActionResult> EscalateServiceRequest(
            string requestCode, [FromBody] EscalateRequest request, CancellationToken ct)
        {
            try
            {
                var sr = await _serviceRequestService.EscalateAsync(
                    requestCode, request.Reason, request.AgentId, ct);

                await _auditService.LogAsync("ServiceRequestEscalated",
                    "ServiceRequest", requestCode, User.Identity?.Name,
                    details: $"Reason: {request.Reason}");

                return Ok(new { requestCode = sr.RequestCode, status = sr.Status.ToString() });
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // RESERVATIONS
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>List reservations.</summary>
        [HttpGet("reservations")]
        public async Task<IActionResult> ListReservations(
            [FromQuery] int skip = 0, [FromQuery] int take = 20,
            [FromQuery] string? status = null, CancellationToken ct = default)
        {
            take = Math.Min(take, 100);
            var query = _db.Reservations.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReservationStatus>(status, true, out var s))
                query = query.Where(r => r.Status == s);

            var reservations = await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .Skip(skip)
                .Take(take)
                .Select(r => new
                {
                    id = r.Id,
                    code = r.ReservationCode,
                    phoneNumber = r.PhoneNumber,
                    userName = r.UserName,
                    airline = r.AirlineName ?? r.AirlineKey,
                    route = $"{r.FromAirport} → {r.ToAirport}",
                    departDate = r.DepartDate,
                    status = r.Status.ToString(),
                    quotedPrice = r.QuotedPrice,
                    currency = r.Currency,
                    createdAt = r.CreatedAtUtc,
                    updatedAt = r.UpdatedAtUtc
                })
                .ToListAsync(ct);

            var total = await _db.Reservations.CountAsync(ct);

            return Ok(new { items = reservations, total, skip, take });
        }

        // ═══════════════════════════════════════════════════════════════════
        // AUDIT LOGS
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>List audit logs.</summary>
        [HttpGet("audit")]
        public async Task<IActionResult> ListAuditLogs(
            [FromQuery] int skip = 0, [FromQuery] int take = 50,
            [FromQuery] string? action = null,
            [FromQuery] string? entityType = null, CancellationToken ct = default)
        {
            take = Math.Min(take, 200);
            var logs = await _auditService.ListAsync(skip, take, action, entityType, ct);

            return Ok(new
            {
                items = logs.Select(a => new
                {
                    id = a.Id,
                    action = a.Action,
                    entityType = a.EntityType,
                    entityId = a.EntityId,
                    actorId = a.ActorId,
                    channel = a.Channel,
                    details = a.Details,
                    ipAddress = a.IpAddress,
                    timestamp = a.Timestamp
                }),
                skip,
                take
            });
        }

        // ═══════════════════════════════════════════════════════════════════
        // KNOWLEDGE ENTRIES
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>List knowledge entries.</summary>
        [HttpGet("knowledge")]
        public async Task<IActionResult> ListKnowledge(
            [FromQuery] int skip = 0, [FromQuery] int take = 20,
            [FromQuery] string? category = null, CancellationToken ct = default)
        {
            take = Math.Min(take, 100);
            var query = _db.KnowledgeEntries.AsQueryable();

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(k => k.Category == category);

            var entries = await query
                .OrderByDescending(k => k.CreatedAtUtc)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);

            return Ok(new { items = entries, skip, take });
        }

        // ═══════════════════════════════════════════════════════════════════
        // HEALTH
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>Detailed health check (authenticated).</summary>
        [HttpGet("health")]
        [AllowAnonymous]
        public async Task<IActionResult> Health(CancellationToken ct)
        {
            var dbHealthy = false;
            try
            {
                dbHealthy = await _db.Database.CanConnectAsync(ct);
            }
            catch { }

            return Ok(new
            {
                status = dbHealthy ? "Healthy" : "Degraded",
                database = dbHealthy ? "Connected" : "Unavailable",
                timestamp = DateTime.UtcNow,
                version = "3.0.0"
            });
        }
    }

    // ── Request DTOs ──────────────────────────────────────────────────────
    public class UpdateStatusRequest
    {
        public ServiceRequestStatus Status { get; set; }
        public string? Message { get; set; }
    }

    public class EscalateRequest
    {
        public string? Reason { get; set; }
        public string? AgentId { get; set; }
    }
}
