using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Implementations;
using Xunit;

namespace WhatsAppBot.Tests.Services
{
    public class PersistentSessionServiceTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly PersistentSessionService _service;

        public PersistentSessionServiceTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            var logger = new Mock<ILogger<PersistentSessionService>>();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["Session:TimeoutMinutes"] = "10080" // 7 days
                })
                .Build();

            _service = new PersistentSessionService(_db, logger.Object, config);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task GetOrCreateSession_CreatesNewSession()
        {
            var session = await _service.GetOrCreateSessionAsync("whatsapp", "2348012345678");

            session.Should().NotBeNull();
            session.Channel.Should().Be("whatsapp");
            session.ConversationId.Should().Be("2348012345678");
            session.CurrentState.Should().Be("New");
            session.IsExpired.Should().BeFalse();
            session.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        }

        [Fact]
        public async Task GetOrCreateSession_ReturnsExistingSession()
        {
            var session1 = await _service.GetOrCreateSessionAsync("whatsapp", "2348012345678");
            var session2 = await _service.GetOrCreateSessionAsync("whatsapp", "2348012345678");

            session1.Id.Should().Be(session2.Id);
        }

        [Fact]
        public async Task GetOrCreateSession_DifferentChannelsGetDifferentSessions()
        {
            var waSession = await _service.GetOrCreateSessionAsync("whatsapp", "2348012345678");
            var tgSession = await _service.GetOrCreateSessionAsync("telegram", "987654");

            waSession.Id.Should().NotBe(tgSession.Id);
        }

        [Fact]
        public async Task UpdateSession_UpdatesActivity()
        {
            var session = await _service.GetOrCreateSessionAsync("whatsapp", "2348012345678");
            var originalActivity = session.LastActivityAtUtc;

            await Task.Delay(50); // Small delay for timestamp difference

            session.CurrentState = "Verified";
            await _service.UpdateSessionAsync(session);

            session.LastActivityAtUtc.Should().BeOnOrAfter(originalActivity);
        }

        [Fact]
        public async Task TransitionStateAsync_UpdatesState()
        {
            var session = await _service.GetOrCreateSessionAsync("telegram", "987654");

            var updated = await _service.TransitionStateAsync(
                session.SessionId, "FlightSearch", "ChooseAirline");

            updated.CurrentState.Should().Be("FlightSearch");
            updated.WorkflowStep.Should().Be("ChooseAirline");
        }

        [Fact]
        public async Task CleanupExpiredSessions_MarksExpiredSessions()
        {
            // Create a session that's already expired
            var expiredSession = new AppSession
            {
                Channel = "whatsapp",
                ConversationId = "expired_user",
                CurrentState = "Verified",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5)
            };
            _db.AppSessions.Add(expiredSession);
            await _db.SaveChangesAsync();

            // Create a valid session
            await _service.GetOrCreateSessionAsync("whatsapp", "valid_user");

            var cleaned = await _service.CleanupExpiredSessionsAsync();

            cleaned.Should().BeGreaterOrEqualTo(1);

            var expired = await _db.AppSessions.FirstAsync(s => s.Id == expiredSession.Id);
            expired.CurrentState.Should().Be("Expired");
        }

        [Fact]
        public async Task GetActiveCountAsync_ReturnsCorrectCount()
        {
            await _service.GetOrCreateSessionAsync("whatsapp", "user1");
            await _service.GetOrCreateSessionAsync("whatsapp", "user2");
            await _service.GetOrCreateSessionAsync("telegram", "user3");

            var count = await _service.GetActiveCountAsync();
            count.Should().BeGreaterOrEqualTo(3);
        }

        [Fact]
        public async Task Touch_UpdatesExpiry()
        {
            var session = new AppSession
            {
                Channel = "test",
                ConversationId = "touch_test"
            };

            var initialExpiry = session.ExpiresAtUtc;

            session.Touch(TimeSpan.FromDays(7));

            session.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddDays(6));
        }

        [Fact]
        public async Task SessionWithUserId_AssociatesWithUser()
        {
            var user = new User { DisplayName = "Test" };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var session = await _service.GetOrCreateSessionAsync(
                "whatsapp", "2348012345678", user.Id);

            session.UserId.Should().Be(user.Id);
        }
    }
}
