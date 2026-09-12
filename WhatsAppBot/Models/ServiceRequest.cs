using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models
{
    /// <summary>
    /// Represents a service request lifecycle from user request to completion.
    /// Supports multiple request types (flight search, booking, support, etc.).
    /// </summary>
    public class ServiceRequest
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Human-readable request code.</summary>
        [Required]
        [MaxLength(30)]
        public string RequestCode { get; set; } = string.Empty;

        /// <summary>Internal user ID.</summary>
        public int? UserId { get; set; }
        public User? User { get; set; }

        /// <summary>Associated session ID.</summary>
        public int? SessionId { get; set; }
        public AppSession? Session { get; set; }

        /// <summary>Channel the request originated from.</summary>
        [MaxLength(20)]
        public string Channel { get; set; } = string.Empty;

        /// <summary>
        /// Request type: FlightSearch, FlightBooking, BookingCancellation,
        /// ProductEnquiry, CustomerSupport, GeneralEnquiry, AiAssistance, HumanEscalation.
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string RequestType { get; set; } = string.Empty;

        /// <summary>Current status of the service request.</summary>
        public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.New;

        [MaxLength(500)]
        public string? StatusMessage { get; set; }

        /// <summary>Summary of the user's request.</summary>
        [MaxLength(2000)]
        public string? RequestSummary { get; set; }

        /// <summary>Result or response data (JSON or text).</summary>
        public string? ResultData { get; set; }

        /// <summary>Linked reservation code if applicable.</summary>
        [MaxLength(30)]
        public string? ReservationCode { get; set; }

        /// <summary>Whether this requires human agent attention.</summary>
        public bool RequiresHumanAgent { get; set; }

        /// <summary>Assigned human agent identifier.</summary>
        [MaxLength(100)]
        public string? AssignedAgentId { get; set; }

        /// <summary>Priority: Low, Normal, High, Urgent.</summary>
        [MaxLength(10)]
        public string Priority { get; set; } = "Normal";

        /// <summary>AI was involved in processing.</summary>
        public bool UsedAI { get; set; }

        /// <summary>AI provider used.</summary>
        [MaxLength(50)]
        public string? AIProvider { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime? EscalatedAtUtc { get; set; }
    }

    public enum ServiceRequestStatus
    {
        New = 0,
        Received = 1,
        Processing = 2,
        AwaitingUser = 3,
        AwaitingVerification = 4,
        AwaitingPayment = 5,
        Assigned = 6,
        Escalated = 7,
        Completed = 8,
        Cancelled = 9,
        Failed = 10
    }
}
