using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Flights
{
    public interface IFlightPricingProvider
    {
        string Name { get; }

        // return null if provider can't quote
        Task<FlightQuote?> TryQuoteAsync(AirlineTarget airline, FlightSearchDraft req, CancellationToken ct);
    }
}
