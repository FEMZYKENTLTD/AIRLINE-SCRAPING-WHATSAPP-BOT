using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models.Learning
{
    public class ConversationInsight
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string PhoneNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? TopicDetected { get; set; }

        [MaxLength(100)]
        public string? IntentDetected { get; set; }

        [MaxLength(50)]
        public string? SentimentDetected { get; set; }

        [MaxLength(50)]
        public string? PreferredAirline { get; set; }

        [MaxLength(20)]
        public string? CommonOrigin { get; set; }

        [MaxLength(20)]
        public string? CommonDestination { get; set; }

        public string? RawConversationJson { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}