using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Flights
{
    public class AmadeusFlightPricingProvider : IFlightPricingProvider
    {
        public string Name => "Amadeus";

        private readonly HttpClient _http;
        private readonly AmadeusOptions _opt;
        private readonly ILogger<AmadeusFlightPricingProvider> _logger;

        public AmadeusFlightPricingProvider(
            HttpClient http,
            IOptions<AmadeusOptions> options,
            ILogger<AmadeusFlightPricingProvider> logger)
        {
            _http = http;
            _opt = options.Value;
            _logger = logger;
        }

        public async Task<FlightQuote?> TryQuoteAsync(AirlineTarget airline, FlightSearchDraft req, CancellationToken ct)
        {
            if (!_opt.Enabled) return null;
            if (string.IsNullOrWhiteSpace(_opt.ClientId) || string.IsNullOrWhiteSpace(_opt.ClientSecret)) return null;
            if (!req.DepartDate.HasValue || string.IsNullOrWhiteSpace(req.From) || string.IsNullOrWhiteSpace(req.To)) return null;

            // OAuth token
            var token = await GetAccessToken(ct);
            if (string.IsNullOrWhiteSpace(token)) return null;

            var baseUrl = _opt.BaseUrl.TrimEnd('/');

            // Flight offers search (basic)
            // NOTE: Cabin, max, currency, etc can be extended later.
            var url =
                $"{baseUrl}/v2/shopping/flight-offers" +
                $"?originLocationCode={Uri.EscapeDataString(req.From!)}" +
                $"&destinationLocationCode={Uri.EscapeDataString(req.To!)}" +
                $"&departureDate={req.DepartDate.Value:yyyy-MM-dd}" +
                $"&adults={Math.Max(1, req.Adults)}" +
                $"&children={Math.Max(0, req.Children)}" +
                $"&infants={Math.Max(0, req.Infants)}" +
                $"&max=5";

            if (req.IsRoundTrip && req.ReturnDate.HasValue)
                url += $"&returnDate={req.ReturnDate.Value:yyyy-MM-dd}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Amadeus offers failed: {Status} {Body}", resp.StatusCode, body);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
                return null;

            // Find cheapest offer
            decimal? cheapest = null;
            string? currency = null;

            foreach (var offer in data.EnumerateArray())
            {
                if (!offer.TryGetProperty("price", out var priceObj)) continue;
                if (!priceObj.TryGetProperty("total", out var totalEl)) continue;
                if (!priceObj.TryGetProperty("currency", out var curEl)) continue;

                var totalStr = totalEl.GetString();
                var curStr = curEl.GetString();

                if (decimal.TryParse(totalStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    if (cheapest == null || val < cheapest.Value)
                    {
                        cheapest = val;
                        currency = curStr;
                    }
                }
            }

            if (cheapest == null) return null;

            return new FlightQuote
            {
                SourceKey = airline.SourceKey,
                IsPriceExact = true,
                Price = cheapest.Value,
                Currency = currency ?? "USD",
                BookingUrl = airline.StartUrl,
                Message = "Cheapest available offer from Amadeus (live pricing)."
            };
        }

        private async Task<string?> GetAccessToken(CancellationToken ct)
        {
            var baseUrl = _opt.BaseUrl.TrimEnd('/');
            var tokenUrl = $"{baseUrl}/v1/security/oauth2/token";

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _opt.ClientId!,
                ["client_secret"] = _opt.ClientSecret!
            });

            using var resp = await _http.PostAsync(tokenUrl, form, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Amadeus token failed: {Status} {Body}", resp.StatusCode, body);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("access_token", out var tokenEl))
                return tokenEl.GetString();

            return null;
        }
    }
}
