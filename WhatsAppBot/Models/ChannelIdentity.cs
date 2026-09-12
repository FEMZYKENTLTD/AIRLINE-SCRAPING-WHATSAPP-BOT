using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models
{
    /// <summary>
    /// Links an internal User to a provider-specific identity
    /// (e.g., WhatsApp phone number, Telegram chat ID).
    /// A single user can have multiple channel identities.
    /// </summary>
    public class ChannelIdentity
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        /// <summary>Channel name: "whatsapp", "telegram", "web".</summary>
        [Required]
        [MaxLength(20)]
        public string Channel { get; set; } = string.Empty;

        /// <summary>Provider-specific user/chat identifier.</summary>
        [Required]
        [MaxLength(100)]
        public string ProviderUserId { get; set; } = string.Empty;

        /// <summary>Display name on the channel.</summary>
        [MaxLength(200)]
        public string? DisplayName { get; set; }

        /// <summary>Provider-specific metadata (JSON).</summary>
        [MaxLength(2000)]
        public string? Metadata { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeenAtUtc { get; set; }
    }
}
