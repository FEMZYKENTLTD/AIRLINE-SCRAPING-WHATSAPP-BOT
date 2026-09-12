using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models
{
    /// <summary>
    /// Persisted message from any channel (inbound or outbound).
    /// Supports idempotency via ProviderMessageId.
    /// </summary>
    public class Message
    {
        [Key]
        public long Id { get; set; }

        /// <summary>Internal session ID.</summary>
        public int? SessionId { get; set; }
        public AppSession? Session { get; set; }

        /// <summary>Internal user ID.</summary>
        public int? UserId { get; set; }
        public User? User { get; set; }

        /// <summary>Channel: "whatsapp", "telegram".</summary>
        [Required]
        [MaxLength(20)]
        public string Channel { get; set; } = string.Empty;

        /// <summary>Provider-specific message ID (for idempotency).</summary>
        [MaxLength(200)]
        public string? ProviderMessageId { get; set; }

        /// <summary>"inbound" or "outbound".</summary>
        [Required]
        [MaxLength(10)]
        public string Direction { get; set; } = string.Empty;

        /// <summary>Message type: text, image, audio, interactive, command.</summary>
        [MaxLength(20)]
        public string MessageType { get; set; } = "text";

        /// <summary>Message text/content.</summary>
        public string? Content { get; set; }

        /// <summary>Channel-specific provider identifier (phone number, chat ID).</summary>
        [MaxLength(100)]
        public string? ProviderUserId { get; set; }

        /// <summary>Correlation ID for tracing.</summary>
        [MaxLength(64)]
        public string? CorrelationId { get; set; }

        /// <summary>Processing status: received, processed, failed, duplicate.</summary>
        [MaxLength(20)]
        public string ProcessingStatus { get; set; } = "received";

        /// <summary>Error information if processing failed.</summary>
        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
