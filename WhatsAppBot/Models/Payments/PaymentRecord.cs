using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppBot.Models.Payments
{
    public class PaymentRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        public string ReservationCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string PhoneNumber { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        [MaxLength(5)]
        public string Currency { get; set; } = "USD";

        [MaxLength(30)]
        public string Provider { get; set; } = "stripe";

        [MaxLength(200)]
        public string? ExternalPaymentId { get; set; }

        [MaxLength(500)]
        public string? PaymentUrl { get; set; }

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        [MaxLength(500)]
        public string? FailureReason { get; set; }

        public decimal? RefundedAmount { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }
    }
}