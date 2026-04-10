namespace WhatsAppBot.Models.Flights
{
    public class FlightQuote
    {
        public string SourceKey { get; set; } = string.Empty;

        // true when price is from API offer; false when it's just deep-link fallback
        public bool IsPriceExact { get; set; }

        public decimal? Price { get; set; }
        public string? Currency { get; set; }

        public string BookingUrl { get; set; } = string.Empty;

        // extra message for user
        public string Message { get; set; } = string.Empty;
    }
}
