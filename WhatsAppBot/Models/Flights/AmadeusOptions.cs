namespace WhatsAppBot.Models.Flights
{
    public class AmadeusOptions
    {
        public bool Enabled { get; set; } = false;

        // From Amadeus dashboard
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }

        // Usually: https://test.api.amadeus.com (test) or https://api.amadeus.com (prod)
        public string BaseUrl { get; set; } = "https://test.api.amadeus.com";
    }
}
