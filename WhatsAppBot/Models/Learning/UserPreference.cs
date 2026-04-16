using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models.Learning
{
    public class UserPreference
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string PreferenceKey { get; set; } = string.Empty;

        [Required]
        public string PreferenceValue { get; set; } = string.Empty;

        public int Confidence { get; set; } = 1;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}