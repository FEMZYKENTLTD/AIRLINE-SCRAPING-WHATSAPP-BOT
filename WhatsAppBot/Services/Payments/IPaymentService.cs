using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models.Payments;

namespace WhatsAppBot.Services.Payments
{
    /// <summary>
    /// Result of a payment operation. A payment is NEVER reported as
    /// successful unless a provider actually created/confirmed it.
    /// </summary>
    public class PaymentResult
    {
        public bool Success { get; init; }
        public string? Reason { get; init; }
        public string? ExternalPaymentId { get; init; }
        public string? PaymentUrl { get; init; }
        public string? ClientSecret { get; init; }
        public PaymentStatus Status { get; init; } = PaymentStatus.Pending;

        /// <summary>True only when the result came from a sandbox/test provider run.</summary>
        public bool Simulated { get; init; }
    }

    /// <summary>
    /// Payment abstraction. The only concrete implementation is Stripe.
    /// When Stripe is not configured the service fails gracefully — it never
    /// invents a successful payment.
    /// </summary>
    public interface IPaymentService
    {
        /// <summary>True when a real provider (Stripe) is configured.</summary>
        bool IsConfigured { get; }

        /// <summary>Provider name, e.g. "stripe" or "none" when unconfigured.</summary>
        string ProviderName { get; }

        /// <summary>
        /// Creates a payment for a reservation. Persists a PaymentRecord.
        /// Never returns Success=true without provider confirmation.
        /// </summary>
        Task<PaymentResult> CreatePaymentAsync(
            string reservationCode,
            string phoneNumber,
            decimal amount,
            string currency,
            CancellationToken ct = default);

        /// <summary>Applies a provider event (succeeded/failed/refunded) to a record.</summary>
        Task<PaymentRecord?> RecordProviderEventAsync(
            string externalPaymentId,
            PaymentStatus newStatus,
            string? failureReason = null,
            CancellationToken ct = default);

        /// <summary>Latest payment record for a reservation.</summary>
        Task<PaymentRecord?> GetByReservationCodeAsync(
            string reservationCode, CancellationToken ct = default);
    }
}
