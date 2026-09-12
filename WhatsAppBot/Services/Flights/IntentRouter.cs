using System;
using System.Text.RegularExpressions;

namespace WhatsAppBot.Services.Flights
{
    /// <summary>
    /// Deterministic, channel-agnostic intent routing for free-text messages.
    ///
    /// Design goals:
    ///  • No LLM needed for the routing decision (fast, testable, safe).
    ///  • The LLM is NEVER given direct access to business actions — flight
    ///    requests are routed to the flight services which execute the real
    ///    work through the provider layer (Amadeus / deep link / scrape).
    ///  • Input for an already-active flight flow is always routed back to
    ///    the flow (checked by the controller via flightFlowActive).
    /// </summary>
    public class IntentRouter : IIntentRouter
    {
        // Flight domain vocabulary
        private static readonly Regex FlightWordRegex = new(
            @"(?ix)\b(?:flights?|fares?|airfare|airfares?|tickets?|airline|airport|fly|flying|cabin|seat|booking|bookings?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Explicit travel-intent verbs
        private static readonly Regex IntentVerbRegex = new(
            @"(?ix)\b(?:book|booking|find|search|look|price|prices|pricing|cost|costs|cheapest|cheaper|reserve|reservation|want|need|get|buy|schedule|when|how much)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "X to Y" route pattern (airport codes or short city names)
        private static readonly Regex RoutePatternRegex = new(
            @"(?ix)\b(?:from\s+)?[a-z0-9]{2,12}\s+(?:to|→|->)\s+[a-z0-9]{2,12}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Explicit IATA pair: "LOS to LHR", "LOS→LHR"
        private static readonly Regex IataPairRegex = new(
            @"\b[A-Z]{3}\s*(?:to|→|->|-)\s*[A-Z]{3}\b",
            RegexOptions.Compiled);

        /// <summary>
        /// Routes a non-command message. The message is routed to the flight
        /// service when it contains an explicit travel request (intent verb +
        /// flight vocabulary, or a route pattern). Anything else goes to the
        /// general AI assistant, which can still talk about flights.
        /// </summary>
        public MessageRoute Route(string text, bool flightFlowActive)
        {
            var message = (text ?? string.Empty).Trim();
            if (message.Length == 0)
                return MessageRoute.AiAssistant;

            if (flightFlowActive)
                return MessageRoute.FlightService;

            if (IataPairRegex.IsMatch(message))
                return MessageRoute.FlightService;

            var hasFlightWord = FlightWordRegex.IsMatch(message);
            var hasIntentVerb = IntentVerbRegex.IsMatch(message);
            var hasRoute = RoutePatternRegex.IsMatch(message);

            // "I want to book a flight", "find flights", "cheapest ticket"
            if (hasFlightWord && hasIntentVerb)
                return MessageRoute.FlightService;

            // "LOS to LHR tomorrow" / "flight from lagos to london"
            if (hasFlightWord && hasRoute)
                return MessageRoute.FlightService;

            return MessageRoute.AiAssistant;
        }
    }
}
