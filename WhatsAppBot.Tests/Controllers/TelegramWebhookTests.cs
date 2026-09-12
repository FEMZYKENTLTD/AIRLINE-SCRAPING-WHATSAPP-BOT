using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WhatsAppBot.Controllers;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;
using Xunit;

namespace WhatsAppBot.Tests.Controllers
{
    /// <summary>
    /// Tests for Telegram webhook processing logic.
    /// Uses mocked dependencies to test controller behavior.
    /// </summary>
    public class TelegramWebhookTests
    {
        [Fact]
        public void TelegramController_Constructor_Succeeds()
        {
            // Verify the controller can be instantiated with all dependencies
            var telegram = new Mock<ITelegramService>();
            var sessionService = new Mock<IPersistentSessionService>();
            var conversationService = new Mock<IConversationService>();
            var userService = new Mock<IUserService>();
            var serviceRequestService = new Mock<IServiceRequestService>();
            var llm = new Mock<ILLMService>();
            var auditService = new Mock<IAuditService>();
            var config = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
            var logger = new Mock<ILogger<TelegramWebhookController>>();

            var controller = new TelegramWebhookController(
                telegram.Object,
                sessionService.Object,
                conversationService.Object,
                userService.Object,
                serviceRequestService.Object,
                llm.Object,
                auditService.Object,
                config.Object,
                logger.Object);

            controller.Should().NotBeNull();
        }

        [Fact]
        public void UserSession_ConversationFlightStep_DefaultsToNone()
        {
            var session = new UserSession();
            session.ConversationFlightStep.Should().Be(WhatsAppBot.Models.Flights.FlightConversationStep.None);
        }

        [Fact]
        public void FlightConversationStep_HasAllExpectedValues()
        {
            var values = Enum.GetValues<WhatsAppBot.Models.Flights.FlightConversationStep>();

            values.Should().Contain(WhatsAppBot.Models.Flights.FlightConversationStep.None);
            values.Should().Contain(WhatsAppBot.Models.Flights.FlightConversationStep.AwaitingMode);
            values.Should().Contain(WhatsAppBot.Models.Flights.FlightConversationStep.AwaitingFrom);
            values.Should().Contain(WhatsAppBot.Models.Flights.FlightConversationStep.AwaitingTo);
            values.Should().Contain(WhatsAppBot.Models.Flights.FlightConversationStep.ReadyToQuote);
        }

        [Fact]
        public void AppSession_Touch_UpdatesExpiry()
        {
            var session = new AppSession();
            var beforeTouch = session.ExpiresAtUtc;

            session.Touch(TimeSpan.FromDays(7));

            session.ExpiresAtUtc.Should().NotBeNull();
            session.ExpiresAtUtc!.Value.Should().BeAfter(DateTime.UtcNow.AddDays(6));
            session.LastActivityAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void AppSession_IsExpired_WhenPastExpiry()
        {
            var session = new AppSession
            {
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1)
            };

            session.IsExpired.Should().BeTrue();
        }

        [Fact]
        public void AppSession_IsNotExpired_WhenFutureExpiry()
        {
            var session = new AppSession
            {
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
            };

            session.IsExpired.Should().BeFalse();
        }

        [Fact]
        public void ServiceRequestStatus_HasAllExpectedValues()
        {
            var values = Enum.GetValues<ServiceRequestStatus>();

            values.Should().Contain(ServiceRequestStatus.New);
            values.Should().Contain(ServiceRequestStatus.Processing);
            values.Should().Contain(ServiceRequestStatus.Escalated);
            values.Should().Contain(ServiceRequestStatus.Completed);
            values.Should().Contain(ServiceRequestStatus.Failed);
            values.Should().Contain(ServiceRequestStatus.Cancelled);
        }
    }
}
