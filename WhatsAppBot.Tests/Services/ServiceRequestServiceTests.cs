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
    public class ServiceRequestServiceTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly ServiceRequestService _service;

        public ServiceRequestServiceTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            var logger = new Mock<ILogger<ServiceRequestService>>();
            _service = new ServiceRequestService(_db, logger.Object);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task CreateAsync_GeneratesUniqueCode()
        {
            var sr1 = await _service.CreateAsync("FlightSearch", "whatsapp");
            var sr2 = await _service.CreateAsync("FlightBooking", "telegram");

            sr1.RequestCode.Should().NotBe(sr2.RequestCode);
            sr1.RequestCode.Should().StartWith("SR-");
        }

        [Fact]
        public async Task CreateAsync_SetsCorrectInitialStatus()
        {
            var sr = await _service.CreateAsync("CustomerSupport", "whatsapp",
                summary: "Need help with booking");

            sr.Status.Should().Be(ServiceRequestStatus.New);
            sr.RequestSummary.Should().Be("Need help with booking");
        }

        [Fact]
        public async Task UpdateStatusAsync_TransitionsCorrectly()
        {
            var sr = await _service.CreateAsync("FlightSearch", "whatsapp");

            var updated = await _service.UpdateStatusAsync(sr.RequestCode,
                ServiceRequestStatus.Processing, "Searching flights");

            updated.Status.Should().Be(ServiceRequestStatus.Processing);
            updated.StatusMessage.Should().Be("Searching flights");
        }

        [Fact]
        public async Task EscalateAsync_SetsEscalatedStatus()
        {
            var sr = await _service.CreateAsync("CustomerSupport", "whatsapp");

            var escalated = await _service.EscalateAsync(sr.RequestCode,
                "User frustrated", "agent-001");

            escalated.Status.Should().Be(ServiceRequestStatus.Escalated);
            escalated.RequiresHumanAgent.Should().BeTrue();
            escalated.AssignedAgentId.Should().Be("agent-001");
            escalated.EscalatedAtUtc.Should().NotBeNull();
        }

        [Fact]
        public async Task CompleteAsync_SetsCompletion()
        {
            var sr = await _service.CreateAsync("FlightSearch", "whatsapp");

            var completed = await _service.CompleteAsync(sr.RequestCode,
                "{\"flights\": []}");

            completed.Status.Should().Be(ServiceRequestStatus.Completed);
            completed.CompletedAtUtc.Should().NotBeNull();
            completed.ResultData.Should().Contain("flights");
        }

        [Fact]
        public async Task GetStatusCountsAsync_GroupsCorrectly()
        {
            await _service.CreateAsync("FlightSearch", "whatsapp");
            await _service.CreateAsync("FlightBooking", "whatsapp");
            await _service.CreateAsync("Support", "telegram");

            var counts = await _service.GetStatusCountsAsync();

            counts.Should().ContainKey(ServiceRequestStatus.New);
            counts[ServiceRequestStatus.New].Should().BeGreaterOrEqualTo(3);
        }

        [Fact]
        public async Task ListAsync_FiltersByStatus()
        {
            var sr1 = await _service.CreateAsync("FlightSearch", "whatsapp");
            var sr2 = await _service.CreateAsync("FlightBooking", "telegram");

            await _service.UpdateStatusAsync(sr1.RequestCode, ServiceRequestStatus.Processing);

            var processing = await _service.ListAsync(0, 100,
                status: ServiceRequestStatus.Processing);
            var all = await _service.ListAsync(0, 100);

            processing.Should().AllSatisfy(r =>
                r.Status.Should().Be(ServiceRequestStatus.Processing));
            all.Count.Should().BeGreaterOrEqualTo(2);
        }

        [Fact]
        public async Task GetByCodeAsync_ReturnsCorrectRequest()
        {
            var created = await _service.CreateAsync("AiAssistance", "telegram",
                summary: "AI query test");

            var found = await _service.GetByCodeAsync(created.RequestCode);

            found.Should().NotBeNull();
            found!.RequestType.Should().Be("AiAssistance");
            found.RequestSummary.Should().Be("AI query test");
        }
    }
}
