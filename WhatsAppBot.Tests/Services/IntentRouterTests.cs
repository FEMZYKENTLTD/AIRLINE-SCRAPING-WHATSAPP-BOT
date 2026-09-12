using FluentAssertions;
using WhatsAppBot.Services.Flights;
using Xunit;

namespace WhatsAppBot.Tests.Services
{
    /// <summary>
    /// Deterministic intent routing — no LLM involved, fully testable.
    /// The same router is used by BOTH channels (multi-channel consistency).
    /// </summary>
    public class IntentRouterTests
    {
        private readonly IntentRouter _router = new();

        [Theory]
        [InlineData("I want a flight from LOS to LHR")]
        [InlineData("Book a flight to London please")]
        [InlineData("find flights from Lagos to Dubai next week")]
        [InlineData("cheapest ticket to New York")]
        [InlineData("I need to fly to Istanbul in December")]
        public void FlightRequestWithIntentVerb_RoutesToFlightService(string text)
        {
            _router.Route(text, flightFlowActive: false)
                .Should().Be(MessageRoute.FlightService);
        }

        [Theory]
        [InlineData("LOS to LHR tomorrow")]
        [InlineData("Lagos to London tickets please")]
        [InlineData("price for LOS-IST in October")]
        public void FlightWordPlusRoute_RoutesToFlightService(string text)
        {
            _router.Route(text, flightFlowActive: false)
                .Should().Be(MessageRoute.FlightService);
        }

        [Fact]
        public void IataPair_RoutesToFlightService()
        {
            _router.Route("LOS to LHR", flightFlowActive: false)
                .Should().Be(MessageRoute.FlightService);
        }

        [Fact]
        public void ActiveFlightFlow_AlwaysRoutesToFlightService()
        {
            // Mid-flow input belongs to the flow regardless of wording
            _router.Route("yes", flightFlowActive: true)
                .Should().Be(MessageRoute.FlightService);
            _router.Route("2026-12-01", flightFlowActive: true)
                .Should().Be(MessageRoute.FlightService);
        }

        [Theory]
        [InlineData("what's the weather in Lagos today")]
        [InlineData("tell me a joke")]
        [InlineData("how do I reset my password")]
        [InlineData("what does your company do")]
        public void NonFlightMessages_RouteToAiAssistant(string text)
        {
            _router.Route(text, flightFlowActive: false)
                .Should().Be(MessageRoute.AiAssistant);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void EmptyInput_RoutesToAiAssistant(string? text)
        {
            _router.Route(text, flightFlowActive: false)
                .Should().Be(MessageRoute.AiAssistant);
        }

        [Fact]
        public void HotelRequest_DoesNotRouteToFlightService()
        {
            // "book a hotel" has the intent verb but no flight vocabulary
            _router.Route("I want to book a hotel in Nairobi", flightFlowActive: false)
                .Should().Be(MessageRoute.AiAssistant);
        }
    }
}
