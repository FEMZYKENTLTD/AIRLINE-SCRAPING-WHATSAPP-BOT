namespace WhatsAppBot.Services.Flights
{
    public class FlightPricingOptions
    {
        // Auto mode tries providers in order:
        // Amadeus -> Scrape -> DeepLink
        public bool EnableAmadeus { get; set; } = false;

        // Amadeus credentials (test or prod)
        public string AmadeusBaseUrl { get; set; } = "https://test.api.amadeus.com";
        public string AmadeusClientId { get; set; } = "";
        public string AmadeusClientSecret { get; set; } = "";

        // Deep link fallback allowed?
        public bool EnableDeepLinkFallback { get; set; } = true;

        // Scrape is best-effort only (no captcha bypass)
        public bool EnableScrapeFallback { get; set; } = true;
    }
}
