using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Data;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;
using WhatsAppBot.Models.Reservations;

namespace WhatsAppBot.Services.Reservations
{
    public class ReservationService : IReservationService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ReservationService> _logger;

        public ReservationService(AppDbContext db, ILogger<ReservationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Reservation> CreateReservationAsync(
            string phoneNumber,
            string? userName,
            string? userEmail,
            FlightSearchDraft draft,
            FlightQuote quote,
            string airlineName,
            CancellationToken ct)
        {
            var reservation = new Reservation
            {
                ReservationCode = GenerateReservationCode(),
                PhoneNumber = phoneNumber,
                UserName = userName,
                UserEmail = userEmail,
                AirlineKey = draft.SourceKey ?? quote.SourceKey,
                AirlineName = airlineName,
                FromAirport = draft.From ?? "",
                ToAirport = draft.To ?? "",
                IsRoundTrip = draft.IsRoundTrip,
                DepartDate = draft.DepartDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                ReturnDate = draft.ReturnDate,
                Adults = draft.Adults,
                Children = draft.Children,
                Infants = draft.Infants,
                QuotedPrice = quote.Price,
                Currency = quote.Currency ?? "USD",
                BookingMethod = quote.IsPriceExact ? "api" : "automation",
                BookingUrl = quote.BookingUrl,
                Status = ReservationStatus.Draft,
                StatusMessage = "Reservation created. Awaiting passenger details.",
                ExpiresAtUtc = DateTime.UtcNow.AddHours(24),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _db.Reservations.Add(reservation);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Reservation {Code} created for {Phone}: {From}→{To} on {Date}",
                reservation.ReservationCode,
                phoneNumber,
                reservation.FromAirport,
                reservation.ToAirport,
                reservation.DepartDate);

            return reservation;
        }

        public async Task<Reservation?> GetByCodeAsync(string reservationCode, CancellationToken ct)
        {
            return await _db.Reservations
                .Include(r => r.Passenger)
                .FirstOrDefaultAsync(r => r.ReservationCode == reservationCode, ct);
        }

        public async Task<List<Reservation>> GetUserReservationsAsync(
            string phoneNumber, int limit, CancellationToken ct)
        {
            return await _db.Reservations
                .Where(r => r.PhoneNumber == phoneNumber)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task<Reservation?> AttachPassengerAsync(
            string reservationCode, PassengerInfo passenger, CancellationToken ct)
        {
            var reservation = await GetByCodeAsync(reservationCode, ct);
            if (reservation == null) return null;

            // Save passenger
            passenger.PhoneNumber = reservation.PhoneNumber;
            passenger.UpdatedAtUtc = DateTime.UtcNow;

            _db.Passengers.Add(passenger);
            await _db.SaveChangesAsync(ct);

            reservation.PassengerId = passenger.Id;
            reservation.Passenger = passenger;
            reservation.Status = ReservationStatus.PendingPayment;
            reservation.StatusMessage = "Passenger details collected. Awaiting payment.";
            reservation.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Passenger attached to reservation {Code}: {Name}",
                reservationCode, passenger.FullName);

            return reservation;
        }

        public async Task<Reservation?> UpdateStatusAsync(
            string reservationCode, ReservationStatus newStatus, string? message, CancellationToken ct)
        {
            var reservation = await GetByCodeAsync(reservationCode, ct);
            if (reservation == null) return null;

            reservation.Status = newStatus;
            reservation.StatusMessage = message;
            reservation.UpdatedAtUtc = DateTime.UtcNow;

            if (newStatus == ReservationStatus.Confirmed)
                reservation.ConfirmedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Reservation {Code} status updated to {Status}",
                reservationCode, newStatus);

            return reservation;
        }

        public async Task<Reservation?> CancelReservationAsync(
            string reservationCode, string reason, CancellationToken ct)
        {
            var reservation = await GetByCodeAsync(reservationCode, ct);
            if (reservation == null) return null;

            if (!reservation.CanCancel)
            {
                _logger.LogWarning(
                    "Cannot cancel reservation {Code}: status is {Status}",
                    reservationCode, reservation.Status);
                return reservation;
            }

            reservation.Status = ReservationStatus.Cancelled;
            reservation.CancellationReason = reason;
            reservation.CancelledAtUtc = DateTime.UtcNow;
            reservation.UpdatedAtUtc = DateTime.UtcNow;
            reservation.StatusMessage = $"Cancelled: {reason}";

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Reservation {Code} cancelled. Reason: {Reason}",
                reservationCode, reason);

            return reservation;
        }

        public string GenerateReservationCode()
        {
            var prefix = "FZK";
            var timestamp = DateTime.UtcNow.ToString("yyMMdd");
            var random = new Random().Next(1000, 9999);
            return $"{prefix}-{timestamp}-{random}";
        }
    }
}