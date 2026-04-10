using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Flights
{
    public class ApiFlightPricingProvider : IFlightPricingProvider
    {
        private readonly HttpClient _http;
        private readonly ILogger<ApiFlightPricingProvider> _logger;
        private readonly FlightPricingOptions _opt;

        public string Name => "ApiPricing";

        public ApiFlightPricingProvider(
            HttpClient http,
            IOptions<FlightPricingOptions> options,
            ILogger<ApiFlightPricingProvider> logger)
        {
            _http = http;
            _logger = logger;
            _opt = options.Value;
        }

        public async Task<FlightQuote?> TryQuoteAsync(AirlineTarget airline, FlightSearchDraft req, CancellationToken ct)
        {
            if (!_opt.EnableApiPricing) return null;
            if (string.IsNullOrWhiteSpace(_opt.ApiBaseUrl)) return null;

            try
            {
                _http.BaseAddress = new Uri(_opt.ApiBaseUrl);

                if (!string.IsNullOrWhiteSpace(_opt.ApiKey))
                {
                    _http.DefaultRequestHeaders.Remove("X-API-KEY");
                    _http.DefaultRequestHeaders.Add("X-API-KEY", _opt.ApiKey);
                }

                // You define this endpoint in YOUR API:
                // POST /quote  body: airline + flight search draft
                var payload = new
                {
                    sourceKey = airline.SourceKey,
                    from = req.From,
                    to = req.To,
                    trip = req.TripType,
                    depart = req.DepartDate?.ToString("yyyy-MM-dd"),
                    ret = req.ReturnDate?.ToString("yyyy-MM-dd"),
                    adults = req.Adults,
                    children = req.Children,
                    infants = req.Infants
                };

                var resp = await _http.PostAsJsonAsync("/quote", payload, ct);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("API pricing failed for {Airline}. Status: {Status}", airline.SourceKey, (int)resp.StatusCode);
                    return null;
                }

                // Expected response shape (you can adjust):
                // { totalPrice: 12345.67, currency: "NGN", bookingUrl: "..." }
                var data = await resp.Content.ReadFromJsonAsync<ApiQuoteResponse>(cancellationToken: ct);

                if (data == null) return null;

                return new FlightQuote
                {
                    SourceKey = airline.SourceKey,
                    IsPriceExact = true,
                    TotalPrice = data.TotalPrice,
                    Currency = data.Currency,
                    BookingUrl = data.BookingUrl ?? airline.StartUrl,
                    Message = "Live price returned from API."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API pricing exception for {Airline}", airline.SourceKey);
                return null;
            }
        }

        private class ApiQuoteResponse
        {
            public decimal TotalPrice { get; set; }
            public string Currency { get; set; } = "NGN";
            public string? BookingUrl { get; set; }
        }
    }
}
