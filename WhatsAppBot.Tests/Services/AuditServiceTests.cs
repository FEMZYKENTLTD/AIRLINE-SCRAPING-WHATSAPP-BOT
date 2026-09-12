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
    public class AuditServiceTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly AuditService _service;

        public AuditServiceTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            var logger = new Mock<ILogger<AuditService>>();
            _service = new AuditService(_db, logger.Object);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task LogAsync_CreatesAuditEntry()
        {
            await _service.LogAsync(
                action: "AdminLogin",
                actorId: "admin",
                ipAddress: "127.0.0.1");

            var logs = await _service.ListAsync(0, 10);
            logs.Should().HaveCount(1);
            logs.First().Action.Should().Be("AdminLogin");
        }

        [Fact]
        public async Task ListAsync_FiltersByAction()
        {
            await _service.LogAsync("AdminLogin", actorId: "admin");
            await _service.LogAsync("StatusChange", entityType: "Reservation");
            await _service.LogAsync("AdminLogin", actorId: "admin2");

            var loginLogs = await _service.ListAsync(0, 100, action: "AdminLogin");
            loginLogs.Should().HaveCount(2);

            var allLogs = await _service.ListAsync(0, 100);
            allLogs.Should().HaveCount(3);
        }

        [Fact]
        public async Task GetByEntityAsync_ReturnsCorrectLogs()
        {
            await _service.LogAsync("StatusChange", "Reservation", "FZK-001");
            await _service.LogAsync("StatusChange", "Reservation", "FZK-002");
            await _service.LogAsync("CancelRequest", "Reservation", "FZK-001");

            var logs = await _service.GetByEntityAsync("Reservation", "FZK-001");
            logs.Should().HaveCount(2);
        }

        [Fact]
        public async Task LogAsync_DoesNotThrow_EvenWithNullFields()
        {
            Func<Task> act = async () => await _service.LogAsync("TestAction");
            await act.Should().NotThrowAsync();
        }
    }
}
