using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models.Passengers
{
    public class PassengerInfo
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? FirstName { get; set; }

        [MaxLength(200)]
        public string? LastName { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        [MaxLength(10)]
        public string? Gender { get; set; }

        [MaxLength(100)]
        public string? Nationality { get; set; }

        [MaxLength(50)]
        public string? PassportNumber { get; set; }

        public DateOnly? PassportExpiry { get; set; }

        [MaxLength(255)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? ContactPhone { get; set; }

        // Preferences
        [MaxLength(20)]
        public string? SeatPreference { get; set; }

        [MaxLength(50)]
        public string? MealPreference { get; set; }

        [MaxLength(100)]
        public string? FrequentFlyerNumber { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(FullName) &&
            !string.IsNullOrWhiteSpace(Email) &&
            !string.IsNullOrWhiteSpace(PassportNumber) &&
            DateOfBirth.HasValue;
    }
}