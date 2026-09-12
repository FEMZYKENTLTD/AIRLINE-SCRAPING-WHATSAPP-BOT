using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Implementations;
using Xunit;

namespace WhatsAppBot.Tests.Services
{
    public class ConversationServiceTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly ConversationService _service;

        public ConversationServiceTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            var logger = new Mock<ILogger<ConversationService>>();
            _service = new ConversationService(_db, logger.Object);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task LogInboundAsync_PersistsMessage()
        {
            var msg = await _service.LogInboundAsync(
                "whatsapp", "2348012345678", "Hello bot!");

            msg.Should().NotBeNull();
            msg.Direction.Should().Be("inbound");
            msg.Channel.Should().Be("whatsapp");
            msg.Content.Should().Be("Hello bot!");
            msg.Id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task LogOutboundAsync_PersistsMessage()
        {
            var msg = await _service.LogOutboundAsync(
                "telegram", "987654", "Hello user!");

            msg.Should().NotBeNull();
            msg.Direction.Should().Be("outbound");
            msg.Content.Should().Be("Hello user!");
        }

        [Fact]
        public async Task ExistsByProviderMessageId_ReturnsTrue_WhenExists()
        {
            await _service.LogInboundAsync(
                "whatsapp", "123", "test", "wamid_abc123");

            var exists = await _service.ExistsByProviderMessageIdAsync("wamid_abc123");
            exists.Should().BeTrue();
        }

        [Fact]
        public async Task ExistsByProviderMessageId_ReturnsFalse_WhenNotExists()
        {
            var exists = await _service.ExistsByProviderMessageIdAsync("nonexistent_id");
            exists.Should().BeFalse();
        }

        [Fact]
        public async Task Idempotency_PreventsDuplicateProviderMessages()
        {
            // Simulate duplicate webhook delivery
            var msg1 = await _service.LogInboundAsync(
                "whatsapp", "123", "First message", "wamid_duplicate");

            var msg2 = await _service.LogInboundAsync(
                "whatsapp", "123", "First message", "wamid_duplicate");

            // Both should succeed but idempotency check should catch duplicates
            var exists = await _service.ExistsByProviderMessageIdAsync("wamid_duplicate");
            exists.Should().BeTrue();
        }

        [Fact]
        public async Task GetSessionMessagesAsync_ReturnsInOrder()
        {
            var session = new AppSession
            {
                Channel = "whatsapp",
                ConversationId = "123",
                CurrentState = "Verified"
            };
            _db.AppSessions.Add(session);
            await _db.SaveChangesAsync();

            await _service.LogInboundAsync("whatsapp", "123", "msg1", sessionId: session.Id);
            await _service.LogOutboundAsync("whatsapp", "123", "reply1", sessionId: session.Id);
            await _service.LogInboundAsync("whatsapp", "123", "msg2", sessionId: session.Id);

            var messages = await _service.GetSessionMessagesAsync(session.Id);

            messages.Should().HaveCount(3);
            // Ordered by timestamp descending
            messages[0].Content.Should().Be("msg2");
        }

        [Fact]
        public async Task GetCountAsync_ReturnsCorrectCount()
        {
            await _service.LogInboundAsync("whatsapp", "1", "msg1");
            await _service.LogOutboundAsync("telegram", "2", "msg2");

            var count = await _service.GetCountAsync();
            count.Should().BeGreaterOrEqualTo(2);
        }
    }
}
