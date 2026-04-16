using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Flights
{
    public class DeepLinkFlightPricingProvider : IFlightPricingProvider
    {
        public string Name => "DeepLink";

        public Task<FlightQuote?> TryQuoteAsync(
            AirlineTarget airline,
            FlightSearchDraft req,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(airline.StartUrl))
                return Task.FromResult<FlightQuote?>(null);

            var bookingUrl = BuildDeepLink(airline, req);

            return Task.FromResult<FlightQuote?>(new FlightQuote
            {
                SourceKey = airline.SourceKey,
                IsPriceExact = false,
                BookingUrl = bookingUrl,
                Message = $"Visit {airline.Name} to view live fares and complete your booking."
            });
        }

        private static string BuildDeepLink(AirlineTarget airline, FlightSearchDraft req)
        {
            var baseUrl = airline.StartUrl.TrimEnd('/');

            return airline.SourceKey.ToLowerInvariant() switch
            {
                "turkish" => $"https://www.turkishairlines.com/en-int/flights/"
                    + $"?origin={req.From}&destination={req.To}"
                    + $"&departure={req.DepartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                    + (req.IsRoundTrip && req.ReturnDate.HasValue
                        ? $"&return={req.ReturnDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                        : "")
                    + $"&adult={req.Adults}&child={req.Children}&infant={req.Infants}",

                "lufthansa" => $"https://www.lufthansa.com/ng/en/flight-search"
                    + $"?origin={req.From}&destination={req.To}"
                    + $"&outbound-date={req.DepartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                    + (req.IsRoundTrip && req.ReturnDate.HasValue
                        ? $"&inbound-date={req.ReturnDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                        : "")
                    + $"&pax={req.Adults}",

                "airpeace" => $"https://flyairpeace.com/booking"
                    + $"?from={req.From}&to={req.To}"
                    + $"&date={req.DepartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                    + $"&adults={req.Adults}",

                "arikair" => $"https://www.arikair.com/plan-and-book"
                    + $"?origin={req.From}&destination={req.To}"
                    + $"&date={req.DepartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",

                _ => baseUrl
            };
        }
    }
}