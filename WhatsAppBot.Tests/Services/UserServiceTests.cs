using System;
using System.Linq;
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
    public class UserServiceTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly UserService _service;

        public UserServiceTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            var logger = new Mock<ILogger<UserService>>();
            _service = new UserService(_db, logger.Object);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task FindOrCreateByChannel_CreatesNewUser_WhenNotExists()
        {
            var (user, identity) = await _service.FindOrCreateByChannelAsync(
                "whatsapp", "2348012345678", "John Doe");

            user.Should().NotBeNull();
            user.Id.Should().BeGreaterThan(0);
            user.DisplayName.Should().Be("John Doe");

            identity.Should().NotBeNull();
            identity.Channel.Should().Be("whatsapp");
            identity.ProviderUserId.Should().Be("2348012345678");
        }

        [Fact]
        public async Task FindOrCreateByChannel_FindsExistingUser_WhenExists()
        {
            var (user1, _) = await _service.FindOrCreateByChannelAsync(
                "whatsapp", "2348012345678", "John Doe");

            var (user2, identity2) = await _service.FindOrCreateByChannelAsync(
                "whatsapp", "2348012345678", "John Updated");

            user2.Id.Should().Be(user1.Id); // Same user
            identity2.LastSeenAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task FindOrCreateByChannel_SameUserDifferentChannels()
        {
            var (user1, _) = await _service.FindOrCreateByChannelAsync(
                "whatsapp", "2348012345678", "John");

            // Different channel, different provider ID = new user
            var (user2, _) = await _service.FindOrCreateByChannelAsync(
                "telegram", "987654", "John");

            user2.Id.Should().NotBe(user1.Id); // Different internal users
        }

        [Fact]
        public async Task UpdateProfile_UpdatesFields()
        {
            var (user, _) = await _service.FindOrCreateByChannelAsync(
                "whatsapp", "2348012345678", "John");

            var updated = await _service.UpdateProfileAsync(user.Id, "Jane Doe", "jane@example.com");

            updated.DisplayName.Should().Be("Jane Doe");
            updated.Email.Should().Be("jane@example.com");
        }

        [Fact]
        public async Task ListAsync_ReturnsPaginatedResults()
        {
            for (int i = 0; i < 15; i++)
            {
                await _service.FindOrCreateByChannelAsync("whatsapp", $"23480000000{i:D2}", $"User {i}");
            }

            var page1 = await _service.ListAsync(0, 10);
            var page2 = await _service.ListAsync(10, 10);

            page1.Should().HaveCount(10);
            page2.Should().HaveCount(5);
        }

        [Fact]
        public async Task ListAsync_SearchFilters()
        {
            await _service.FindOrCreateByChannelAsync("whatsapp", "234801111111", "Alice Smith");
            await _service.FindOrCreateByChannelAsync("whatsapp", "234802222222", "Bob Jones");
            await _service.FindOrCreateByChannelAsync("telegram", "999", "Charlie");

            var results = await _service.ListAsync(0, 100, "Alice");
            results.Should().HaveCount(1);
            results.First().DisplayName.Should().Be("Alice Smith");
        }

        [Fact]
        public async Task GetCountAsync_ReturnsCorrectCount()
        {
            await _service.FindOrCreateByChannelAsync("whatsapp", "111", "User 1");
            await _service.FindOrCreateByChannelAsync("telegram", "222", "User 2");

            var count = await _service.GetCountAsync();
            count.Should().BeGreaterOrEqualTo(2);
        }
    }
}
