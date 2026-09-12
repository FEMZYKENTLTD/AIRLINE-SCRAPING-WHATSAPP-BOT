using System;
using System.Collections.Generic;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;

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

        // ===== Flight Search (WhatsApp webhook controller flow) =====
        public FlightSearchDraft FlightDraft { get; set; } = new();
        public FlightStep FlightStep { get; set; } = FlightStep.None;
        public FlightPricingMode FlightPricingMode { get; set; } = FlightPricingMode.Auto;

        // ===== Flight Conversation (FlightConversationService flow) =====
        public FlightConversationStep ConversationFlightStep { get; set; } = FlightConversationStep.None;

        // ===== Booking =====
        public FlightQuote? CurrentQuote { get; set; }
        public PassengerInfo PassengerDraft { get; set; } = new();
        public string? ActiveReservationCode { get; set; }
        public string? PendingCancellationCode { get; set; }

        public void UpdateActivity() => LastActivity = DateTime.UtcNow;

        public void ResetFlightFlow()
        {
            FlightDraft.Reset();
            FlightStep = FlightStep.None;
            FlightPricingMode = FlightPricingMode.Auto;
            CurrentQuote = null;
        }

        public void ResetBookingFlow()
        {
            PassengerDraft = new PassengerInfo();
            ActiveReservationCode = null;
            CurrentQuote = null;
        }

        public void ResetAll()
        {
            ResetFlightFlow();
            ResetBookingFlow();
            PendingCancellationCode = null;
        }
    }
}