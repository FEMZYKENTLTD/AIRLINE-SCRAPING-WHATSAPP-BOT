namespace WhatsAppBot.Services.Flights
{
    public class FlightPricingOptions
    {
        // If you later add your own pricing API
        public bool EnableApiPricing { get; set; } = false;
        public string? ApiBaseUrl { get; set; }
        public string? ApiKey { get; set; }

        // allow booking deep-links when API isn't available
        public bool EnableDeepLinkFallback { get; set; } = true;
    }
}
