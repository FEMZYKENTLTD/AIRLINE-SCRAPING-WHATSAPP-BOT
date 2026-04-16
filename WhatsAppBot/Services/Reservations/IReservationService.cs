using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;
using WhatsAppBot.Models.Reservations;

namespace WhatsAppBot.Services.Reservations
{
    public interface IReservationService
    {
        Task<Reservation> CreateReservationAsync(
            string phoneNumber,
            string? userName,
            string? userEmail,
            FlightSearchDraft draft,
            FlightQuote quote,
            string airlineName,
            CancellationToken ct);

        Task<Reservation?> GetByCodeAsync(string reservationCode, CancellationToken ct);

        Task<List<Reservation>> GetUserReservationsAsync(
            string phoneNumber,
            int limit,
            CancellationToken ct);

        Task<Reservation?> AttachPassengerAsync(
            string reservationCode,
            PassengerInfo passenger,
            CancellationToken ct);

        Task<Reservation?> UpdateStatusAsync(
            string reservationCode,
            ReservationStatus newStatus,
            string? message,
            CancellationToken ct);

        Task<Reservation?> CancelReservationAsync(
            string reservationCode,
            string reason,
            CancellationToken ct);

        string GenerateReservationCode();
    }
}