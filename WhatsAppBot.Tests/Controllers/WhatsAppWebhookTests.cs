using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WhatsAppBot.Controllers;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Flights;
using WhatsAppBot.Services.Automation;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Media;
using WhatsAppBot.Services.Reservations;
using WhatsAppBot.Services.Scraping;
using Xunit;

namespace WhatsAppBot.Tests.Controllers
{
    /// <summary>
    /// Security + idempotency tests for the WhatsApp webhook (HMAC verify,
    /// duplicate delivery handling, verification handshake).
    /// </summary>
    public class WhatsAppWebhookTests : IDisposable
    {
        private const string TestAppSecret = "unit-test-app-secret-do-not-use-in-prod";

        private readonly Mock<ILLMService> _llm = new();
        private readonly Mock<IWhatsAppService> _whatsApp = new();
        private readonly Mock<IUserService> _userService = new();
        private readonly Mock<IPersistentSessionService> _sessionService = new();
        private readonly Mock<IConversationService> _conversationService = new();
        private readonly Mock<IServiceRequestService> _serviceRequests = new();
        private readonly Mock<IAuditService> _auditService = new();
        private readonly Mock<IIntentRouter> _intentRouter = new();
        private readonly Mock<IChatLogService> _chatLog = new();
        private readonly Mock<IProductCatalogService> _catalog = new();
        private readonly FlightPricingService _flightPricing;
        private readonly Mock<IReservationService> _reservations = new();
        private readonly BookingOrchestrator _orchestrator;
        private readonly Mock<IImageGenerationService> _imageService = new();
        private readonly Mock<IConfiguration> _config = new();
        private readonly Mock<ILogger<WhatsAppWebhookController>> _logger = new();
        private readonly FlightConversationService _flightConversation;

        public WhatsAppWebhookTests()
        {
            var pricing = new FlightPricingService(
                Array.Empty<IFlightPricingProvider>(),
                Options.Create(new FlightPricingOptions()),
                new Mock<ILogger<FlightPricingService>>().Object);
            _flightConversation = new FlightConversationService(
                pricing,
                Options.Create(new ScrapingOptions()),
                new Mock<ILogger<FlightConversationService>>().Object);
            _flightPricing = pricing;
            _orchestrator = new BookingOrchestrator(
                Array.Empty<IFlightBookingAutomation>(),
                new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>().Object,
                new Mock<ILogger<BookingOrchestrator>>().Object);
        }

        public void Dispose() { }

        private WhatsAppWebhookController CreateController(string? appSecret = TestAppSecret)
        {
            _config.Setup(c => c["MetaWhatsApp:AppSecret"]).Returns(appSecret);
            _config.Setup(c => c["MetaWhatsApp:VerifyToken"]).Returns("verify-token-123");
            _config.Setup(c => c["Bot:Name"]).Returns("TestBot");
            _config.Setup(c => c["Bot:Company"]).Returns("TestCo");
            _config.Setup(c => c["Bot:SupportEmail"]).Returns("support@test.com");

            // Default: not a duplicate
            _conversationService
                .Setup(s => s.ExistsByProviderMessageIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            return new WhatsAppWebhookController(
                _llm.Object, _whatsApp.Object, _userService.Object,
                _sessionService.Object, _conversationService.Object,
                _serviceRequests.Object, _auditService.Object,
                _intentRouter.Object, _flightConversation,
                _chatLog.Object, _catalog.Object,
                _flightPricing, _reservations.Object,
                _orchestrator,
                Options.Create(new ScrapingOptions()),
                _logger.Object, _config.Object, _imageService.Object);
        }

        private static void SetBody(DefaultHttpContext ctx, string body)
        {
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        }

        private static string Sign(string secret, string body)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // GET /webhook — Meta verification handshake
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public void Verify_ValidToken_ReturnsChallenge()
        {
            var controller = CreateController();
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = controller.Verify("subscribe", "verify-token-123", "challenge-abc");

            result.Should().BeOfType<ContentResult>();
            ((ContentResult)result).Content.Should().Be("challenge-abc");
        }

        [Fact]
        public void Verify_WrongToken_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = controller.Verify("subscribe", "wrong-token", "challenge-abc");

            result.Should().BeOfType<UnauthorizedResult>();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // POST /webhook — HMAC signature enforcement
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task Receive_InvalidSignature_Returns401_AuditsRejection()
        {
            var controller = CreateController();
            var ctx = new DefaultHttpContext();
            controller.ControllerContext = new ControllerContext { HttpContext = ctx };

            var body = SampleWebhookPayload("2348012345678", "hello", "wamid.TEST001");
            SetBody(ctx, body);
            ctx.Request.Headers["X-Hub-Signature-256"] = "sha256=deadbeef";

            var result = await controller.Receive();

            result.Should().BeOfType<UnauthorizedResult>();
            _auditService.Verify(
                a => a.LogAsync(
                    "WEBHOOK_REJECTED", "Webhook", null,
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Receive_ValidSignature_ProcessesMessage()
        {
            var controller = CreateController();
            var ctx = new DefaultHttpContext();
            controller.ControllerContext = new ControllerContext { HttpContext = ctx };

            // Existing verified user with an active (persisted) flow context
            var user = new User { Id = 1, DisplayName = "John", Email = "j@x.com" };
            var verifiedFlow = FlowContext.FromUserSession(new UserSession
            {
                PhoneNumber = "2348012345678",
                Name = "John",
                Email = "j@x.com",
                State = UserState.Verified
            });
            var session = new AppSession
            {
                Id = 10,
                SessionId = "S1",
                CurrentState = "Verified",
                ContextData = verifiedFlow.ToJson()
            };

            _userService
                .Setup(u => u.FindOrCreateByChannelAsync("whatsapp", "2348012345678", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((user, new ChannelIdentity { Id = 1, UserId = 1 }, true));
            _sessionService
                .Setup(s => s.GetOrCreateSessionAsync("whatsapp", "2348012345678", 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);
            _conversationService
                .Setup(c => c.LogInboundAsync("whatsapp", "2348012345678", "hi there", "wamid.TEST002", 10, 1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Message { Id = 100 });
            _conversationService
                .Setup(c => c.GetSessionMessagesAsync(10, 20, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.Collections.Generic.List<Message>());
            // Route any non-command text to AI
            _intentRouter
                .Setup(r => r.Route("hi there", false))
                .Returns(MessageRoute.AiAssistant);
            _llm.Setup(l => l.GetResponseAsync(It.IsAny<UserSession>(), "hi there"))
                .ReturnsAsync("Hello John!");

            var body = SampleWebhookPayload("2348012345678", "hi there", "wamid.TEST002");
            SetBody(ctx, body);
            ctx.Request.Headers["X-Hub-Signature-256"] = Sign(TestAppSecret, body);

            var result = await controller.Receive();

            result.Should().BeOfType<OkObjectResult>();
            // Message persisted (inbound + outbound)
            _conversationService.Verify(c => c.LogInboundAsync(
                "whatsapp", "2348012345678", "hi there", "wamid.TEST002", 10, 1,
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _conversationService.Verify(c => c.LogOutboundAsync(
                "whatsapp", "2348012345678", "Hello John!", 10, 1,
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            // Reply sent via WhatsApp
            _whatsApp.Verify(w => w.SendMessageAsync("2348012345678", "Hello John!"), Times.Once);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Idempotency — duplicate provider message must not re-process
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task Receive_DuplicateProviderMessage_DoesNotReprocess()
        {
            var controller = CreateController();
            var ctx = new DefaultHttpContext();
            controller.ControllerContext = new ControllerContext { HttpContext = ctx };

            // This provider message ID was already seen
            _conversationService
                .Setup(c => c.ExistsByProviderMessageIdAsync("wamid.DUPLICATE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var body = SampleWebhookPayload("2348012345678", "book me a flight", "wamid.DUPLICATE");
            SetBody(ctx, body);
            ctx.Request.Headers["X-Hub-Signature-256"] = Sign(TestAppSecret, body);

            var result = await controller.Receive();

            result.Should().BeOfType<OkObjectResult>();
            // No user lookup, no inbound log, no LLM call for the duplicate
            _userService.Verify(
                u => u.FindOrCreateByChannelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _conversationService.Verify(
                c => c.LogInboundAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _llm.Verify(l => l.GetResponseAsync(It.IsAny<UserSession>(), It.IsAny<string?>()), Times.Never);
            _auditService.Verify(
                a => a.LogAsync("WEBHOOK_DUPLICATE", "Message", "wamid.DUPLICATE",
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // FlowContext — persisted flow state survives serialization round-trip
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public void FlowContext_RoundTrip_PreservesFlightState()
        {
            var session = new UserSession
            {
                PhoneNumber = "2348012345678",
                Name = "Jane Doe",
                Email = "jane@example.com",
                State = UserState.Verified,
                FlightStep = WhatsAppBot.Models.Flights.FlightStep.DepartDate,
                FlightPricingMode = WhatsAppBot.Models.Flights.FlightPricingMode.Amadeus,
                FlightDraft = new WhatsAppBot.Models.Flights.FlightSearchDraft
                {
                    SourceKey = "turkish",
                    From = "LOS",
                    To = "LHR",
                    IsRoundTrip = true,
                    DepartDate = new DateOnly(2026, 12, 1),
                    ReturnDate = new DateOnly(2026, 12, 15),
                    Adults = 2,
                    Children = 1
                },
                CurrentQuote = new WhatsAppBot.Models.Flights.FlightQuote
                {
                    SourceKey = "turkish",
                    Price = 450000m,
                    Currency = "NGN",
                    IsPriceExact = false,
                    BookingUrl = "https://www.turkishairlines.com",
                    Message = "est"
                }
            };

            var flow = FlowContext.FromUserSession(session);
            var json = flow.ToJson();
            var restored = FlowContext.FromJson(json).ToUserSession(session.PhoneNumber);

            restored.State.Should().Be(UserState.Verified);
            restored.FlightStep.Should().Be(WhatsAppBot.Models.Flights.FlightStep.DepartDate);
            restored.FlightPricingMode.Should().Be(WhatsAppBot.Models.Flights.FlightPricingMode.Amadeus);
            restored.FlightDraft.From.Should().Be("LOS");
            restored.FlightDraft.To.Should().Be("LHR");
            restored.FlightDraft.IsRoundTrip.Should().BeTrue();
            restored.FlightDraft.DepartDate.Should().Be(new DateOnly(2026, 12, 1));
            restored.FlightDraft.Adults.Should().Be(2);
            restored.CurrentQuote.Should().NotBeNull();
            restored.CurrentQuote!.Price.Should().Be(450000m);
        }

        [Fact]
        public void FlowContext_CorruptJson_ReturnsFreshContext()
        {
            var flow = FlowContext.FromJson("{ not valid json !!");

            flow.Should().NotBeNull();
            flow.State.Should().Be(UserState.New);
        }

        // ═══════════════════════════════════════════════════════════════════════
        private static string SampleWebhookPayload(string from, string text, string id) =>
            $@"{{
                ""object"": ""whatsapp"",
                ""entry"": [{{
                    ""id"": ""entry1"",
                    ""changes"": [{{
                        ""field"": ""messages"",
                        ""value"": {{
                            ""messaging_product"": ""whatsapp"",
                            ""messages"": [{{
                                ""from"": ""{from}"",
                                ""id"": ""{id}"",
                                ""timestamp"": ""1700000000"",
                                ""type"": ""text"",
                                ""text"": {{ ""body"": ""{text}"" }}
                            }}]
                        }}
                    }}]
                }}]
            }}";
    }
}
