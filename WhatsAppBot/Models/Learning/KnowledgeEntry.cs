using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models.Learning
{
    /// <summary>
    /// A piece of knowledge the bot has learned.
    /// Sources: scraped airline data, past conversations, manual entries
    /// </summary>
    public class KnowledgeEntry
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string Question { get; set; } = string.Empty;

        [Required]
        public string Answer { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Source { get; set; } = "scraped";

        [MaxLength(200)]
        public string? SourceUrl { get; set; }

        [MaxLength(50)]
        public string? AirlineKey { get; set; }

        /// <summary>
        /// Vector embedding stored as JSON array for similarity search.
        /// Used for semantic matching: "cheapest flight" matches "lowest fare"
        /// </summary>
        public string? EmbeddingJson { get; set; }

        public int UseCount { get; set; }
        public double Relevance { get; set; } = 1.0;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAtUtc { get; set; }
    }
}