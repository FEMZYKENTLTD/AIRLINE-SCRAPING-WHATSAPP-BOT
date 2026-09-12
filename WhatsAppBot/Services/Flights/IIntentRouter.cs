namespace WhatsAppBot.Services.Flights
{
    /// <summary>
    /// Destination of a non-command user message in the shared application layer.
    /// Both WhatsApp and Telegram route through this so business logic stays
    /// centralized (no per-channel copies of the rules).
    /// </summary>
    public enum MessageRoute
    {
        /// <summary>No structured match — go to the general AI assistant.</summary>
        AiAssistant = 0,

        /// <summary>Deterministic flight intent detected — route to the flight service.</summary>
        FlightService = 1
    }

    /// <summary>
    /// Channel-agnostic intent routing. Shared by WhatsApp and Telegram so
    /// both channels apply the same business rules.
    /// </summary>
    public interface IIntentRouter
    {
        /// <summary>
        /// Route a non-command message. When <paramref name="flightFlowActive"/>
        /// is true the input belongs to the in-flight flow and is always routed
        /// to the flight service.
        /// </summary>
        MessageRoute Route(string text, bool flightFlowActive);
    }
}
