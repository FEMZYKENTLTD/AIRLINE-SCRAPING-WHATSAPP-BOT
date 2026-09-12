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
    }
}
