using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models
{
    /// <summary>
    /// Represents an internal user who may interact through multiple channels
    /// (WhatsApp, Telegram, web, etc.).
    /// </summary>
    public class User
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Internal unique identifier for the user.</summary>
        [Required]
        [MaxLength(50)]
        public string ExternalId { get; set; } = Guid.NewGuid().ToString("N");

        [MaxLength(100)]
        public string? DisplayName { get; set; }

        [MaxLength(255)]
        public string? Email { get; set; }

        public bool EmailVerified { get; set; }

        [MaxLength(50)]
        public string? PhoneNumber { get; set; }

        /// <summary>User status: Active, Suspended, Deleted.</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Active";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastActivityAtUtc { get; set; }

        [MaxLength(1000)]
        public string? Metadata { get; set; }

        // Navigation
        public ICollection<ChannelIdentity> ChannelIdentities { get; set; } = new List<ChannelIdentity>();
        public ICollection<UserPreference> Preferences { get; set; } = new List<UserPreference>();
    }
}
