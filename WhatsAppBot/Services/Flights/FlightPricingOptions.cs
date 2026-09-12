namespace WhatsAppBot.Services.Flights
{
    /// <summary>
    /// Configuration for flight pricing provider behavior.
    /// Bound from appsettings.json section "FlightPricing".
    /// </summary>
    public class FlightPricingOptions
    {
        /// <summary>Allow Amadeus API provider.</summary>
        public bool EnableAmadeus { get; set; } = false;

        /// <summary>Allow booking deep-links when API isn't available.</summary>
        public bool EnableDeepLinkFallback { get; set; } = true;

        /// <summary>Allow best-effort scrape-based pricing.</summary>
        public bool EnableScrapeFallback { get; set; } = true;

        // ── Optional self-hosted pricing API (ApiFlightPricingProvider) ──
        // Disabled by default. When enabled, the provider POSTs the search
        // draft to {ApiBaseUrl}/quote and expects
        // { "totalPrice": 1234.56, "currency": "NGN", "bookingUrl": "..." }.

        /// <summary>Enable the self-hosted API pricing provider.</summary>
        public bool EnableApiPricing { get; set; } = false;

        /// <summary>Base URL of the self-hosted pricing API (e.g. https://api.example.com).</summary>
        public string? ApiBaseUrl { get; set; }

        /// <summary>Optional API key sent as the X-API-KEY header.</summary>
        public string? ApiKey { get; set; }
    }
}
