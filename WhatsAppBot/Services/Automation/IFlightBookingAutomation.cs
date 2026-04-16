using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;

namespace WhatsAppBot.Services.Automation
{
    public interface IFlightBookingAutomation
    {
        string AirlineKey { get; }

        Task<FlightQuote?> SearchAsync(
            FlightSearchDraft draft,
            CancellationToken ct);

        Task<bool> ReserveAsync(
            FlightSearchDraft draft,
            PassengerInfo passenger,
            CancellationToken ct);

        Task<bool> CancelAsync(
            string bookingReference,
            CancellationToken ct);
    }
}