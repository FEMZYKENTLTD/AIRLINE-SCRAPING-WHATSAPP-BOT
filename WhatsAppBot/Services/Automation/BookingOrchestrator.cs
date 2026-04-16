using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;
using WhatsAppBot.Models.Reservations;
using WhatsAppBot.Services.Reservations;

namespace WhatsAppBot.Services.Automation
{
    public class BookingOrchestrator
    {
        private readonly IEnumerable<IFlightBookingAutomation> _automations;
        private readonly IReservationService _reservations;
        private readonly ILogger<BookingOrchestrator> _logger;

        public BookingOrchestrator(
            IEnumerable<IFlightBookingAutomation> automations,
            IReservationService reservations,
            ILogger<BookingOrchestrator> logger)
        {
            _automations = automations;
            _reservations = reservations;
            _logger = logger;
        }

        public async Task<FlightQuote?> SearchFlightAsync(
            string airlineKey,
            FlightSearchDraft draft,
            CancellationToken ct)
        {
            var automation = _automations
                .FirstOrDefault(a => a.AirlineKey.Equals(airlineKey, StringComparison.OrdinalIgnoreCase));

            if (automation == null)
            {
                _logger.LogWarning("No automation found for airline: {Key}", airlineKey);
                return null;
            }

            try
            {
                return await automation.SearchAsync(draft, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automation search failed for {Key}", airlineKey);
                return null;
            }
        }

        public async Task<bool> ExecuteBookingAsync(
            string reservationCode,
            FlightSearchDraft draft,
            PassengerInfo passenger,
            CancellationToken ct)
        {
            var reservation = await _reservations.GetByCodeAsync(reservationCode, ct);
            if (reservation == null) return false;

            await _reservations.UpdateStatusAsync(
                reservationCode, ReservationStatus.BookingInProgress,
                "Automation is processing your booking...", ct);

            var automation = _automations
                .FirstOrDefault(a => a.AirlineKey.Equals(
                    reservation.AirlineKey, StringComparison.OrdinalIgnoreCase));

            if (automation == null)
            {
                await _reservations.UpdateStatusAsync(
                    reservationCode, ReservationStatus.Failed,
                    "No automation available for this airline.", ct);
                return false;
            }

            try
            {
                var success = await automation.ReserveAsync(draft, passenger, ct);

                if (success)
                {
                    await _reservations.UpdateStatusAsync(
                        reservationCode, ReservationStatus.Confirmed,
                        "Booking confirmed successfully!", ct);
                }
                else
                {
                    await _reservations.UpdateStatusAsync(
                        reservationCode, ReservationStatus.Failed,
                        "Automation could not complete the booking. Please try manually.", ct);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Booking automation failed for {Code}", reservationCode);

                await _reservations.UpdateStatusAsync(
                    reservationCode, ReservationStatus.Failed,
                    $"Booking failed: {ex.Message}", ct);

                return false;
            }
        }

        public async Task<bool> ExecuteCancellationAsync(
            string reservationCode,
            string reason,
            CancellationToken ct)
        {
            var reservation = await _reservations.GetByCodeAsync(reservationCode, ct);
            if (reservation == null) return false;

            if (!reservation.CanCancel)
            {
                _logger.LogWarning("Cannot cancel {Code}: status {Status}",
                    reservationCode, reservation.Status);
                return false;
            }

            // Try automated cancellation if external ref exists
            if (!string.IsNullOrWhiteSpace(reservation.ExternalBookingRef))
            {
                var automation = _automations
                    .FirstOrDefault(a => a.AirlineKey.Equals(
                        reservation.AirlineKey, StringComparison.OrdinalIgnoreCase));

                if (automation != null)
                {
                    try
                    {
                        await automation.CancelAsync(reservation.ExternalBookingRef, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Automated cancellation failed for {Code}", reservationCode);
                    }
                }
            }

            await _reservations.CancelReservationAsync(reservationCode, reason, ct);
            return true;
        }
    }
}