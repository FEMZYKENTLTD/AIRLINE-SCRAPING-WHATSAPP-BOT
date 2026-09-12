using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models
{
    /// <summary>
    /// Tracks AI/LLM interactions for observability and debugging.
    /// Does not store sensitive prompt content by default.
    /// </summary>
    public class AiInteractionLog
    {
        [Key]
        public long Id { get; set; }

        /// <summary>Internal user ID if available.</summary>
        public int? UserId { get; set; }

        /// <summary>Session ID if available.</summary>
        public int? SessionId { get; set; }

        /// <summary>AI provider name (e.g., "AzureOpenAI", "OpenAI").</summary>
        [MaxLength(50)]
        public string Provider { get; set; } = string.Empty;

        /// <summary>Model/deployment used.</summary>
        [MaxLength(100)]
        public string? Model { get; set; }

        /// <summary>Request category (e.g., "chat", "classification", "knowledge_search").</summary>
        [MaxLength(50)]
        public string? RequestCategory { get; set; }

        /// <summary>Whether the request succeeded.</summary>
        public bool Success { get; set; } = true;

        /// <summary>HTTP status code from the AI provider.</summary>
        public int? ProviderStatusCode { get; set; }

        /// <summary>Token usage if available (JSON: {"prompt":N,"completion":N}).</summary>
        [MaxLength(200)]
        public string? TokenUsage { get; set; }

        /// <summary>Response latency in milliseconds.</summary>
        public long LatencyMs { get; set; }

        /// <summary>Error message if failed.</summary>
        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }

        /// <summary>Whether fallback was used.</summary>
        public bool UsedFallback { get; set; }

        /// <summary>Correlation ID for tracing.</summary>
        [MaxLength(64)]
        public string? CorrelationId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
