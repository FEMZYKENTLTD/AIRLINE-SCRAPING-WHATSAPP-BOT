using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Flights
{
    public class ScrapeFlightPricingProvider : IFlightPricingProvider
    {
        public string Name => "Scrape";

        private readonly HttpClient _http;
        private readonly FlightPricingOptions _opt;
        private readonly ILogger<ScrapeFlightPricingProvider> _logger;

        // Very loose patterns (best-effort)
        private static readonly Regex PriceRegex = new(@"(₦|NGN|\$|USD)\s*([0-9][0-9,\.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public ScrapeFlightPricingProvider(
            HttpClient http,
            IOptions<FlightPricingOptions> options,
            ILogger<ScrapeFlightPricingProvider> logger)
        {
            _http = http;
            _opt = options.Value;
            _logger = logger;
        }

        public async Task<FlightQuote?> TryQuoteAsync(AirlineTarget airline, FlightSearchDraft req, CancellationToken ct)
        {
            if (!_opt.EnableScrapeFallback) return null;

            try
            {
                _http.Timeout = TimeSpan.FromSeconds(20);
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("WhatsAppBotScraper/1.0");

                // Real talk: airline pricing is usually JS + session + captcha.
                // Here we only try to scrape the homepage/marketing pages for any visible "from ₦xxx" promos.
                var html = await _http.GetStringAsync(airline.StartUrl, ct);

                var m = PriceRegex.Match(html);
                if (!m.Success) return null;

                var curRaw = m.Groups[1].Value.ToUpperInvariant();
                var amtRaw = m.Groups[2].Value.Replace(",", "");

                if (!decimal.TryParse(amtRaw, out var amt)) return null;

                var currency = curRaw.Contains("₦") || curRaw.Contains("NGN") ? "NGN" : "USD";

                return new FlightQuote
                {
                    SourceKey = airline.SourceKey,
                    Provider = Name,
                    IsPriceExact = false,
                    Amount = amt,
                    Currency = currency,
                    BookingUrl = airline.StartUrl,
                    Message =
                        "I found a promo-like price on the site (NOT a guaranteed live fare).\n" +
                        "For real fares, use Amadeus or the booking page."
                };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Scrape provider failed for {Airline}", airline.SourceKey);
                return null;
            }
        }
    }
}
