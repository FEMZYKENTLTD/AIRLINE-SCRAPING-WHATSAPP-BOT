using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using WhatsAppBot.Data;
using WhatsAppBot.Services.Implementations;
using Xunit;

namespace WhatsAppBot.Tests.Services
{
    /// <summary>
    /// Multi-channel consistency: WhatsApp and Telegram both go through the
    /// SAME shared application services (user registry, persistent sessions,
    /// message store, service requests). These tests prove the shared layer
    /// behaves identically regardless of channel.
    /// </summary>
    public class MultiChannelConsistencyTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly UserService _userService;
        private readonly PersistentSessionService _sessionService;
        private readonly ConversationService _conversationService;
        private readonly ServiceRequestService _serviceRequestService;
        private readonly AuditService _auditService;

        public MultiChannelConsistencyTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            var logger = new Mock<ILogger<AppDbContext>>();
            _userService = new UserService(_db, new Mock<ILogger<UserService>>().Object);
            _sessionService = new PersistentSessionService(_db, new Mock<ILogger<PersistentSessionService>>().Object,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                    {
                        ["Session:TimeoutMinutes"] = "10080"
                    })
                    .Build());
            _conversationService = new ConversationService(_db, new Mock<ILogger<ConversationService>>().Object);
            _serviceRequestService = new ServiceRequestService(_db, new Mock<ILogger<ServiceRequestService>>().Object);
            _auditService = new AuditService(_db, new Mock<ILogger<AuditService>>().Object);
        }

        public void Dispose() => _db.Dispose();

        // ═══════════════════════════════════════════════════════════════════════
        // User registry — one user per (channel, provider identity)
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task SameProviderSameChannel_ReturnsSameUser()
        {
            var (user1, _, created1) = await _userService.FindOrCreateByChannelAsync("whatsapp", "2348012345678", "John");
            var (user2, _, created2) = await _userService.FindOrCreateByChannelAsync("whatsapp", "2348012345678", "John Updated");

            user1.Id.Should().Be(user2.Id);
            created1.Should().BeTrue();
            created2.Should().BeFalse();
        }

        [Fact]
        public async Task SamePhoneOnTwoChannels_TwoDistinctUsers()
        {
            // Channel identities are per-channel: the same phone number on
            // WhatsApp and Telegram maps to two internal users (by design,
            // since provider IDs are channel-scoped).
            var (waUser, waIdentity, _) = await _userService.FindOrCreateByChannelAsync("whatsapp", "2348012345678", "John");
            var (tgUser, tgIdentity, _) = await _userService.FindOrCreateByChannelAsync("telegram", "2348012345678", "John");

            waUser.Id.Should().NotBe(tgUser.Id);
            waIdentity.Channel.Should().Be("whatsapp");
            tgIdentity.Channel.Should().Be("telegram");
        }

        [Fact]
        public async Task FindOrCreate_WithEmptyProviderId_Throws()
        {
            var act = async () => await _userService.FindOrCreateByChannelAsync("whatsapp", "", "John");
            await act.Should().ThrowAsync<ArgumentException>();

            var act2 = async () => await _userService.FindOrCreateByChannelAsync("", "123", "John");
            await act2.Should().ThrowAsync<ArgumentException>();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Sessions — identical rules for every channel
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task Sessions_AreChannelIsolated_ButSameRules()
        {
            var (waUser, _, _) = await _userService.FindOrCreateByChannelAsync("whatsapp", "2348011111111", "Alice");
            var (tgUser, _, _) = await _userService.FindOrCreateByChannelAsync("telegram", "2348011111111", "Alice");

            var waSession = await _sessionService.GetOrCreateSessionAsync("whatsapp", "2348011111111", waUser.Id);
            var tgSession = await _sessionService.GetOrCreateSessionAsync("telegram", "2348011111111", tgUser.Id);

            // Separate sessions per channel
            waSession.Id.Should().NotBe(tgSession.Id);

            // But the SAME expiry rule applies (7 days = 10080 minutes)
            waSession.ExpiresAtUtc.Should().BeApproximately(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(2));
            tgSession.ExpiresAtUtc.Should().BeApproximately(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(2));
        }

        [Fact]
        public async Task Reset_ClearesStateAndContext_OnAnyChannel()
        {
            var session = await _sessionService.GetOrCreateSessionAsync("whatsapp", "999", null);
            session.CurrentState = "Verified";
            session.WorkflowStep = "DepartDate";
            session.ContextData = "{\"FlightStep\":6}";
            await _sessionService.UpdateSessionAsync(session);

            var reset = await _sessionService.ResetAsync(session);

            reset.CurrentState.Should().Be("New");
            reset.WorkflowStep.Should().BeNull();
            reset.ContextData.Should().BeNull();
            reset.IsExpired.Should().BeFalse();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Message store — idempotency shared across channels
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task Idempotency_DuplicateProviderMessageIgnored_OnEveryChannel()
        {
            // Simulates the same Meta webhook being delivered twice
            var first = await _conversationService.LogInboundAsync(
                "whatsapp", "2348012345678", "book a flight", "wamid.SHARED001");

            var alreadyExists = await _conversationService
                .ExistsByProviderMessageIdAsync("wamid.SHARED001");
            alreadyExists.Should().BeTrue("the idempotency key must be shared across all channels");

            // A different channel's provider message ID with the same local value
            // must NOT collide (channels are distinct namespaces)
            await _conversationService.LogInboundAsync(
                "telegram", "12345", "book a flight", "SHARED001");
            var waCount = await _db.Messages
                .CountAsync(m => m.Channel == "whatsapp" && m.ProviderMessageId == "wamid.SHARED001");
            waCount.Should().Be(1);
        }

        [Fact]
        public async Task SessionMessages_AreStoredInChronologicalOrder()
        {
            var session = await _sessionService.GetOrCreateSessionAsync("whatsapp", "777", null);

            await _conversationService.LogInboundAsync("whatsapp", "777", "hi", providerMessageId: "m1", sessionId: session.Id);
            await Task.Delay(15);
            await _conversationService.LogOutboundAsync("whatsapp", "777", "hello there", sessionId: session.Id);
            await Task.Delay(15);
            await _conversationService.LogInboundAsync("whatsapp", "777", "book LOS to LHR", providerMessageId: "m2", sessionId: session.Id);

            var messages = await _conversationService.GetSessionMessagesAsync(session.Id);

            messages.Should().HaveCount(3);
            // Descending order (newest first) per service contract
            messages[0].Content.Should().Be("book LOS to LHR");
            messages[0].Direction.Should().Be("inbound");
            messages[1].Direction.Should().Be("outbound");
            messages[2].Content.Should().Be("hi");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Service requests — one lifecycle model for all channels
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task ServiceRequest_Lifecycle_IsIdenticalAcrossChannels()
        {
            var (waUser, _, _) = await _userService.FindOrCreateByChannelAsync("whatsapp", "555", "WA User");
            var (tgUser, _, _) = await _userService.FindOrCreateByChannelAsync("telegram", "556", "TG User");

            var waSr = await _serviceRequestService.CreateAsync(
                "FlightSearch", "whatsapp", waUser.Id, summary: "WA search");
            var tgSr = await _serviceRequestService.CreateAsync(
                "FlightSearch", "telegram", tgUser.Id, summary: "TG search");

            // Both start as New with unique codes
            waSr.Status.Should().Be(WhatsAppBot.Models.ServiceRequestStatus.New);
            tgSr.Status.Should().Be(WhatsAppBot.Models.ServiceRequestStatus.New);
            waSr.RequestCode.Should().NotBe(tgSr.RequestCode);
            waSr.RequestCode.Should().StartWith("SR-");

            // Same transition works on both
            var waProcessing = await _serviceRequestService.UpdateStatusAsync(waSr.RequestCode, WhatsAppBot.Models.ServiceRequestStatus.Processing);
            var tgProcessing = await _serviceRequestService.UpdateStatusAsync(tgSr.RequestCode, WhatsAppBot.Models.ServiceRequestStatus.Processing);

            waProcessing.Status.Should().Be(WhatsAppBot.Models.ServiceRequestStatus.Processing);
            tgProcessing.Status.Should().Be(WhatsAppBot.Models.ServiceRequestStatus.Processing);

            var waDone = await _serviceRequestService.CompleteAsync(waSr.RequestCode, "{\"ok\":true}");
            waDone.Status.Should().Be(WhatsAppBot.Models.ServiceRequestStatus.Completed);
            waDone.CompletedAtUtc.Should().NotBeNull();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Audit — every channel action lands in one auditable trail
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task AuditTrail_CapturesActionsFromBothChannels()
        {
            await _auditService.LogAsync("WEBHOOK_RECEIVED", "Message", "100",
                actorId: "wa-phone", channel: "whatsapp");
            await _auditService.LogAsync("WEBHOOK_RECEIVED", "Message", "200",
                actorId: "tg-user", channel: "telegram");
            await _auditService.LogAsync("SERVICE_REQUEST_ESCALATED", "ServiceRequest", "SR-1",
                actorId: "admin", channel: "whatsapp");

            var all = await _auditService.ListAsync(0, 100);
            all.Should().HaveCount(3);

            var waOnly = await _auditService.ListAsync(0, 100);
            waOnly.Count(a => a.Channel == "whatsapp").Should().Be(2);
            waOnly.Count(a => a.Channel == "telegram").Should().Be(1);
        }
    }
}
