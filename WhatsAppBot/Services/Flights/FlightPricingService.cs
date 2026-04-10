using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Flights
{
    public class FlightPricingService
    {
        private readonly IEnumerable<IFlightPricingProvider> _providers;
        private readonly FlightPricingOptions _opt;
        private readonly ILogger<FlightPricingService> _logger;

        public FlightPricingService(
            IEnumerable<IFlightPricingProvider> providers,
            IOptions<FlightPricingOptions> options,
            ILogger<FlightPricingService> logger)
        {
            _providers = providers;
            _opt = options.Value;
            _logger = logger;
        }

        public async Task<FlightQuote> GetQuoteAsync(
            AirlineTarget airline,
            FlightSearchDraft req,
            FlightPricingMode mode,
            CancellationToken ct)
        {
            // Determine provider order
            var ordered = _providers
                .OrderBy(p => p.Name == "Amadeus" ? 0 : 1) // Amadeus first
                .ThenBy(p => p.Name == "DeepLink" ? 1 : 0)
                .ToList();

            foreach (var provider in ordered)
            {
                ct.ThrowIfCancellationRequested();

                // mode filtering
                if (mode == FlightPricingMode.Amadeus && provider.Name != "Amadeus") continue;
                if (mode == FlightPricingMode.DeepLink && provider.Name != "DeepLink") continue;

                // deep link toggle
                if (provider.Name == "DeepLink" && !_opt.EnableDeepLinkFallback) continue;

                try
                {
                    var q = await provider.TryQuoteAsync(airline, req, ct);
                    if (q != null) return q;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pricing provider {Provider} failed", provider.Name);
                }
            }

            // absolute fallback
            return new FlightQuote
            {
                SourceKey = airline.SourceKey,
                IsPriceExact = false,
                BookingUrl = airline.StartUrl,
                Message = "Could not fetch live pricing right now. Please use the airline booking page to view fares."
            };
        }
    }
}
