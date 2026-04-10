namespace WhatsAppBot.Models.Flights
{
    public enum FlightPricingMode
    {
        Auto = 0,     // Try providers in best order
        Amadeus = 1,  // Force Amadeus API provider
        DeepLink = 2  // Force booking deep-link fallback
    }
}
