using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using WhatsAppBot.Data;
using WhatsAppBot.Models.Payments;

namespace WhatsAppBot.Services.Payments
{
    /// <summary>
    /// Stripe-backed payment service.
    ///
    /// SAFETY RULES:
    ///  • If STRIPE_SECRET_KEY is missing → IsConfigured=false and operations
    ///    return a clean failure (Cancelled status) — never a fake success.
    ///  • A PaymentRecord is always persisted so the admin dashboard can audit
    ///    every attempted payment.
    ///  • Statuses come from the provider (or this class when the provider is
    ///    absent); nothing is invented.
    /// </summary>
    public class StripePaymentService : IPaymentService
    {
        private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
        {
            "BIF", "CLF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW",
            "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF",
            "NGN" // Stripe treats NGN as zero-decimal
        };

        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<StripePaymentService> _logger;

        public StripePaymentService(AppDbContext db, IConfiguration config, ILogger<StripePaymentService> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

        public string ProviderName => IsConfigured ? "stripe" : "none";

        public bool IsConfigured
        {
            get
            {
                var key = _config["Stripe:SecretKey"] ?? string.Empty;
                return !string.IsNullOrWhiteSpace(key);
            }
        }

        public async Task<PaymentResult> CreatePaymentAsync(
            string reservationCode,
            string phoneNumber,
            decimal amount,
            string currency,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(reservationCode))
                throw new ArgumentException("reservationCode is required", nameof(reservationCode));
            if (amount <= 0)
                throw new ArgumentException("amount must be positive", nameof(amount));

            // ── Provider not configured → fail gracefully, never fake success ─
            if (!IsConfigured)
            {
                _logger.LogWarning(
                    "Payment requested for {Reservation} but Stripe is not configured (STRIPE_SECRET_KEY missing). Payment recorded as Cancelled.",
                    reservationCode);

                PersistRecord(reservationCode, phoneNumber, amount, currency,
                    "none", null, null, PaymentStatus.Cancelled,
                    "Payment provider (Stripe) is not configured. No real payment was processed.");

                return new PaymentResult
                {
                    Success = false,
                    Reason = "Payment provider (Stripe) is not configured.",
                    Status = PaymentStatus.Cancelled,
                    Simulated = false
                };
            }

            // ── Real Stripe call ──────────────────────────────────────────
            try
            {
                StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];

                var service = new PaymentIntentService(); // Stripe.net v46 naming (singular)
                var intent = await service.CreateAsync(new PaymentIntentCreateOptions
                {
                    Amount = ToSmallestUnit(amount, currency),
                    Currency = currency.ToLowerInvariant(),
                    Metadata = new Dictionary<string, string>
                    {
                        ["reservationCode"] = reservationCode,
                        ["phoneNumber"] = phoneNumber
                    }
                }, options: null, ct: ct);

                var record = PersistRecord(reservationCode, phoneNumber, amount, currency,
                    "stripe", intent.Id, intent.ClientSecret, PaymentStatus.Pending, null);

                _logger.LogInformation("Stripe PaymentIntent {IntentId} created for reservation {Reservation}",
                    intent.Id, reservationCode);

                return new PaymentResult
                {
                    Success = true,
                    ExternalPaymentId = intent.Id,
                    ClientSecret = intent.ClientSecret,
                    Status = PaymentStatus.Pending,
                    Simulated = false
                };
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe PaymentIntent creation failed for {Reservation}", reservationCode);

                PersistRecord(reservationCode, phoneNumber, amount, currency,
                    "stripe", null, null, PaymentStatus.Failed,
                    Truncate($"Stripe error: {ex.StripeError?.Message ?? ex.Message}"));

                return new PaymentResult
                {
                    Success = false,
                    Reason = "Payment provider rejected the request. Please try again.",
                    Status = PaymentStatus.Failed,
                    Simulated = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected payment failure for {Reservation}", reservationCode);

                PersistRecord(reservationCode, phoneNumber, amount, currency,
                    "stripe", null, null, PaymentStatus.Failed,
                    Truncate($"Payment failed: {ex.Message}"));

                return new PaymentResult
                {
                    Success = false,
                    Reason = "Payment could not be processed. Please try again.",
                    Status = PaymentStatus.Failed,
                    Simulated = false
                };
            }
        }

        public async Task<PaymentRecord?> RecordProviderEventAsync(
            string externalPaymentId,
            PaymentStatus newStatus,
            string? failureReason = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(externalPaymentId))
                return null;

            var record = await _db.Payments
                .FirstOrDefaultAsync(p => p.ExternalPaymentId == externalPaymentId, ct);
            if (record == null)
            {
                _logger.LogWarning("Payment event for unknown external id {Id}", externalPaymentId);
                return null;
            }

            record.Status = newStatus;
            record.UpdatedAtUtc = DateTime.UtcNow;

            if (newStatus is PaymentStatus.Succeeded or PaymentStatus.Refunded or PaymentStatus.PartialRefund)
                record.CompletedAtUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(failureReason))
                record.FailureReason = Truncate(failureReason);

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Payment {Id} status updated to {Status}", externalPaymentId, newStatus);
            return record;
        }

        public async Task<PaymentRecord?> GetByReservationCodeAsync(
            string reservationCode, CancellationToken ct = default)
        {
            return await _db.Payments
                .Where(p => p.ReservationCode == reservationCode)
                .OrderByDescending(p => p.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private PaymentRecord PersistRecord(
            string reservationCode, string phoneNumber, decimal amount, string currency,
            string provider, string? externalId, string? clientSecret,
            PaymentStatus status, string? failureReason)
        {
            var record = new PaymentRecord
            {
                ReservationCode = reservationCode,
                PhoneNumber = phoneNumber,
                Amount = amount,
                Currency = currency,
                Provider = provider,
                ExternalPaymentId = externalId,
                PaymentUrl = clientSecret, // Client secret is not a public URL; kept for reference only
                Status = status,
                FailureReason = failureReason,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled
                    ? DateTime.UtcNow : null
            };

            _db.Payments.Add(record);
            _db.SaveChanges();
            return record;
        }

        private static long ToSmallestUnit(decimal amount, string currency)
        {
            if (ZeroDecimalCurrencies.Contains(currency))
                return (long)Math.Round(amount, 0);
            return (long)Math.Round(amount * 100m, 0);
        }

        private static string Truncate(string value, int max = 480) =>
            value.Length <= max ? value : value[..max];
    }
}
