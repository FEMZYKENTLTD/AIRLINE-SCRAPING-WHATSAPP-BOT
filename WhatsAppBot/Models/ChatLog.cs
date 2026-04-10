using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models
{
    public class ChatLog
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string PhoneNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Name { get; set; }

        [MaxLength(255)]
        public string? Email { get; set; }

        [Required]
        public string Message { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        public string Direction { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string MessageType { get; set; } = "text";

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}