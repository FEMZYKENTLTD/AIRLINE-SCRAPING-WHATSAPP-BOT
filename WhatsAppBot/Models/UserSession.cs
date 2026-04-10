using System;
using System.Collections.Generic;
using WhatsAppBot.Models.Flights;

namespace WhatsAppBot.Models
{
    public class UserSession
    {
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Email { get; set; }
        public UserState State { get; set; } = UserState.New;

        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<ChatMessage> ConversationHistory { get; set; } = new();

        // ===== Flights =====
        public FlightSearchDraft FlightDraft { get; set; } = new();
        public FlightStep FlightStep { get; set; } = FlightStep.None;
        public FlightPricingMode FlightPricingMode { get; set; } = FlightPricingMode.Auto;

        public void UpdateActivity() => LastActivity = DateTime.UtcNow;

        public void ResetFlightFlow()
        {
            FlightDraft.Reset();
            FlightStep = FlightStep.None;
            FlightPricingMode = FlightPricingMode.Auto;
        }
    }
}
