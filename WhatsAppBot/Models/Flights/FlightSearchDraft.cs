using System;

namespace WhatsAppBot.Models.Flights
{
    public class FlightSearchDraft
    {
        // Which airline to use (matches Scraping:Airlines[].SourceKey)
        public string? SourceKey { get; set; }

        // Route (IATA codes like LOS, LHR)
        public string? From { get; set; }
        public string? To { get; set; }

        // Trip type
        public bool IsRoundTrip { get; set; }

        // Dates (DateOnly avoids timezone bugs)
        public DateOnly? DepartDate { get; set; }
        public DateOnly? ReturnDate { get; set; }

        // Passengers
        public int Adults { get; set; } = 1;
        public int Children { get; set; } = 0;
        public int Infants { get; set; } = 0;

        public void Reset()
        {
            SourceKey = null;
            From = null;
            To = null;
            IsRoundTrip = false;
            DepartDate = null;
            ReturnDate = null;
            Adults = 1;
            Children = 0;
            Infants = 0;
        }
    }
}
