using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Implementations;
using WhatsAppBot.Services.Interfaces;
using Xunit;

namespace WhatsAppBot.Tests.Services
{
    public class ResilientLlmServiceTests
    {
        private readonly Mock<ILLMService> _innerMock;
        private readonly ResilientLlmService _service;

        public ResilientLlmServiceTests()
        {
            _innerMock = new Mock<ILLMService>();
            var logger = new Mock<ILogger<ResilientLlmService>>();
            _service = new ResilientLlmService(_innerMock.Object, logger.Object);
        }

        [Fact]
        public async Task GetResponseAsync_ReturnsNormalResponse_WhenServiceWorks()
        {
            _innerMock.Setup(s => s.GetResponseAsync(It.IsAny<UserSession>(), It.IsAny<string>()))
                .ReturnsAsync("Hello! How can I help?");

            var session = new UserSession { PhoneNumber = "123", State = UserState.Verified };
            var result = await _service.GetResponseAsync(session, "Hi");

            result.Should().Be("Hello! How can I help?");
        }

        [Fact]
        public async Task GetResponseAsync_ReturnsFallback_WhenServiceThrows()
        {
            _innerMock.Setup(s => s.GetResponseAsync(It.IsAny<UserSession>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Azure OpenAI unavailable"));

            var session = new UserSession { PhoneNumber = "123", State = UserState.Verified };
            var result = await _service.GetResponseAsync(session, "Hello");

            // Should return a helpful fallback, not throw
            result.Should().NotBeNullOrEmpty();
            result.Should().Contain("help");
        }

        [Fact]
        public async Task GetResponseAsync_FallbackSuggestsFlight_ForFlightQuery()
        {
            _innerMock.Setup(s => s.GetResponseAsync(It.IsAny<UserSession>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Service down"));

            var session = new UserSession { PhoneNumber = "123", State = UserState.Verified };
            var result = await _service.GetResponseAsync(session, "I want to book a flight");

            result.Should().Contain("/flight");
        }

        [Fact]
        public async Task GetResponseAsync_FallbackSuggestsProducts_ForProductQuery()
        {
            _innerMock.Setup(s => s.GetResponseAsync(It.IsAny<UserSession>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Service down"));

            var session = new UserSession { PhoneNumber = "123", State = UserState.Verified };
            var result = await _service.GetResponseAsync(session, "Show me your products");

            result.Should().Contain("/products");
        }

        [Fact]
        public async Task GetResponseAsync_FallbackNeverClaimsSuccess()
        {
            _innerMock.Setup(s => s.GetResponseAsync(It.IsAny<UserSession>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Service down"));

            var session = new UserSession { PhoneNumber = "123", State = UserState.Verified };
            var result = await _service.GetResponseAsync(session, "Book me a flight to London");

            // Fallback should NOT fabricate a booking
            result.Should().NotContain("booked");
            result.Should().NotContain("confirmed");
            result.Should().NotContain("reservation created");
        }

        [Fact]
        public async Task GetResponseAsync_HandlesNullMessage()
        {
            _innerMock.Setup(s => s.GetResponseAsync(It.IsAny<UserSession>(), It.IsAny<string>()))
                .ReturnsAsync("Send me a message!");

            var session = new UserSession { PhoneNumber = "123", State = UserState.Verified };
            var result = await _service.GetResponseAsync(session, null);

            result.Should().NotBeNullOrEmpty();
        }
    }
}
