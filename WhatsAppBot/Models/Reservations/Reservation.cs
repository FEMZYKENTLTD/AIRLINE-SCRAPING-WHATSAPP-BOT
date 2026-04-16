using System;
using System.ComponentModel.DataAnnotations;
using WhatsAppBot.Models.Passengers;

namespace WhatsAppBot.Models.Reservations
{
    public class Reservation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        public string ReservationCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string PhoneNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? UserName { get; set; }

        [MaxLength(255)]
        public string? UserEmail { get; set; }

        // Flight Details
        [Required]
        [MaxLength(50)]
        public string AirlineKey { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? AirlineName { get; set; }

        [Required]
        [MaxLength(5)]
        public string FromAirport { get; set; } = string.Empty;

        [Required]
        [MaxLength(5)]
        public string ToAirport { get; set; } = string.Empty;

        public bool IsRoundTrip { get; set; }

        public DateOnly DepartDate { get; set; }
        public DateOnly? ReturnDate { get; set; }

        public int Adults { get; set; } = 1;
        public int Children { get; set; }
        public int Infants { get; set; }

        // Pricing
        public decimal? QuotedPrice { get; set; }

        [MaxLength(5)]
        public string Currency { get; set; } = "USD";

        public decimal? PaidAmount { get; set; }

        // Booking method used
        [MaxLength(30)]
        public string BookingMethod { get; set; } = "api";

        [MaxLength(2000)]
        public string? BookingUrl { get; set; }

        [MaxLength(100)]
        public string? ExternalBookingRef { get; set; }

        // Passenger
        public int? PassengerId { get; set; }
        public PassengerInfo? Passenger { get; set; }

        // Payment
        [MaxLength(100)]
        public string? PaymentIntentId { get; set; }

        // Status
        public ReservationStatus Status { get; set; } = ReservationStatus.Draft;

        [MaxLength(500)]
        public string? StatusMessage { get; set; }

        [MaxLength(500)]
        public string? CancellationReason { get; set; }

        // Timestamps
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ConfirmedAtUtc { get; set; }
        public DateTime? CancelledAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }

        // Helper
        public bool IsActive =>
            Status is ReservationStatus.Draft
                or ReservationStatus.PendingPayment
                or ReservationStatus.PaymentReceived
                or ReservationStatus.BookingInProgress
                or ReservationStatus.Confirmed
                or ReservationStatus.TicketIssued;

        public bool CanCancel =>
            Status is ReservationStatus.Draft
                or ReservationStatus.PendingPayment
                or ReservationStatus.PaymentReceived
                or ReservationStatus.Confirmed;

        public string GetSummary()
        {
            var trip = IsRoundTrip ? "Round-trip" : "One-way";
            var price = QuotedPrice.HasValue ? $"{Currency} {QuotedPrice.Value:N0}" : "TBD";
            var ret = IsRoundTrip && ReturnDate.HasValue ? $"\nReturn: {ReturnDate.Value:yyyy-MM-dd}" : "";

            return $@"📋 *Reservation {ReservationCode}*

✈️ {AirlineName ?? AirlineKey}
📍 {FromAirport} → {ToAirport} ({trip})
📅 Depart: {DepartDate:yyyy-MM-dd}{ret}
👥 {Adults}A {Children}C {Infants}I
💰 {price}
📊 Status: *{Status}*";
        }
    }
}