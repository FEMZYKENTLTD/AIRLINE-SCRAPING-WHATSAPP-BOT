using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models
{
    /// <summary>
    /// Tracks important administrative and security events for audit purposes.
    /// </summary>
    public class AuditLog
    {
        [Key]
        public long Id { get; set; }

        /// <summary>Action performed (e.g., "AdminLogin", "StatusChange", "RecordModified").</summary>
        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        /// <summary>Entity type affected (e.g., "User", "Reservation", "ServiceRequest").</summary>
        [MaxLength(100)]
        public string? EntityType { get; set; }

        /// <summary>Entity ID affected.</summary>
        [MaxLength(50)]
        public string? EntityId { get; set; }

        /// <summary>Actor who performed the action (admin username, system, or user ID).</summary>
        [MaxLength(100)]
        public string? ActorId { get; set; }

        /// <summary>Channel if applicable.</summary>
        [MaxLength(20)]
        public string? Channel { get; set; }

        /// <summary>Additional context/details (JSON).</summary>
        public string? Details { get; set; }

        /// <summary>IP address of the actor.</summary>
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        /// <summary>Correlation ID for tracing.</summary>
        [MaxLength(64)]
        public string? CorrelationId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
