using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models
{
    /// <summary>
    /// Persistent session tracking user interaction state.
    /// Supports 7-day inactivity expiration based on persisted activity timestamps.
    /// </summary>
    public class AppSession
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Unique session identifier.</summary>
        [Required]
        [MaxLength(64)]
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Channel: "whatsapp", "telegram".</summary>
        [Required]
        [MaxLength(20)]
        public string Channel { get; set; } = string.Empty;

        /// <summary>Provider-specific conversation/chat identifier.</summary>
        [MaxLength(100)]
        public string? ConversationId { get; set; }

        /// <summary>Associated internal user ID.</summary>
        public int? UserId { get; set; }
        public User? User { get; set; }

        /// <summary>Channel identity used in this session.</summary>
        public int? ChannelIdentityId { get; set; }
        public ChannelIdentity? ChannelIdentity { get; set; }

        /// <summary>Current workflow state (e.g., New, Onboarding, Verified, FlightSearch).</summary>
        [MaxLength(50)]
        public string CurrentState { get; set; } = "New";

        /// <summary>Active workflow step within the current state.</summary>
        [MaxLength(50)]
        public string? WorkflowStep { get; set; }

        /// <summary>Serialized session context/data (JSON).</summary>
        public string? ContextData { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAtUtc { get; set; }

        /// <summary>Whether this session has expired.</summary>
        public bool IsExpired => ExpiresAtUtc.HasValue && ExpiresAtUtc.Value < DateTime.UtcNow;

        /// <summary>
        /// Update activity timestamp and recalculate expiry.
        /// Default timeout: 7 days of inactivity.
        /// </summary>
        public void Touch(TimeSpan? timeout = null)
        {
            LastActivityAtUtc = DateTime.UtcNow;
            ExpiresAtUtc = DateTime.UtcNow + (timeout ?? TimeSpan.FromDays(7));
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }
}
