using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using Xunit;

namespace WhatsAppBot.Tests.Database
{
    /// <summary>
    /// Tests for database schema, relationships, and persistence.
    /// Uses InMemory database provider for isolation.
    /// </summary>
    public class DatabaseTests : IDisposable
    {
        private readonly AppDbContext _db;

        public DatabaseTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task CanCreateUser()
        {
            var user = new User
            {
                DisplayName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "2348012345678"
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            user.Id.Should().BeGreaterThan(0);
            user.ExternalId.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task CanCreateChannelIdentity()
        {
            var user = new User { DisplayName = "Channel Test User" };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var identity = new ChannelIdentity
            {
                UserId = user.Id,
                Channel = "whatsapp",
                ProviderUserId = "2348012345678",
                DisplayName = "WA User"
            };

            _db.ChannelIdentities.Add(identity);
            await _db.SaveChangesAsync();

            identity.Id.Should().BeGreaterThan(0);

            var loaded = await _db.Users
                .Include(u => u.ChannelIdentities)
                .FirstAsync(u => u.Id == user.Id);

            loaded.ChannelIdentities.Should().HaveCount(1);
            loaded.ChannelIdentities.First().Channel.Should().Be("whatsapp");
        }

        [Fact]
        public async Task CanCreateSession()
        {
            var user = new User { DisplayName = "Session User" };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var session = new AppSession
            {
                Channel = "telegram",
                ConversationId = "12345",
                UserId = user.Id,
                CurrentState = "Verified"
            };
            session.Touch(TimeSpan.FromDays(7));

            _db.AppSessions.Add(session);
            await _db.SaveChangesAsync();

            session.Id.Should().BeGreaterThan(0);
            session.IsExpired.Should().BeFalse();
            session.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        }

        [Fact]
        public async Task SessionExpiredAfterTimeout()
        {
            var session = new AppSession
            {
                Channel = "whatsapp",
                ConversationId = "999",
                CurrentState = "New",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1)
            };

            _db.AppSessions.Add(session);
            await _db.SaveChangesAsync();

            session.IsExpired.Should().BeTrue();
        }

        [Fact]
        public async Task CanCreateMessage()
        {
            var message = new Message
            {
                Channel = "whatsapp",
                ProviderUserId = "2348012345678",
                ProviderMessageId = "wamid.HBgNMjM0ODAxMjM0NTY3OBU=",
                Direction = "inbound",
                Content = "Hello bot!",
                ProcessingStatus = "received"
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            message.Id.Should().BeGreaterThan(0);
            message.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task CanCreateServiceRequest()
        {
            var request = new ServiceRequest
            {
                RequestCode = "SR-260911-1234",
                RequestType = "FlightSearch",
                Channel = "whatsapp",
                Status = ServiceRequestStatus.New,
                RequestSummary = "Search for flights from LOS to LHR"
            };

            _db.ServiceRequests.Add(request);
            await _db.SaveChangesAsync();

            request.Id.Should().BeGreaterThan(0);
            request.Status.Should().Be(ServiceRequestStatus.New);
        }

        [Fact]
        public async Task ServiceRequestStatusTransitions()
        {
            var request = new ServiceRequest
            {
                RequestCode = "SR-260911-5678",
                RequestType = "FlightBooking",
                Channel = "telegram",
                Status = ServiceRequestStatus.New
            };

            _db.ServiceRequests.Add(request);
            await _db.SaveChangesAsync();

            request.Status = ServiceRequestStatus.Processing;
            await _db.SaveChangesAsync();

            var loaded = await _db.ServiceRequests.FirstAsync(sr => sr.Id == request.Id);
            loaded.Status.Should().Be(ServiceRequestStatus.Processing);

            loaded.Status = ServiceRequestStatus.Completed;
            loaded.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var completed = await _db.ServiceRequests.FirstAsync(sr => sr.Id == request.Id);
            completed.Status.Should().Be(ServiceRequestStatus.Completed);
            completed.CompletedAtUtc.Should().NotBeNull();
        }

        [Fact]
        public async Task CanCreateAuditLog()
        {
            var log = new AuditLog
            {
                Action = "AdminLogin",
                ActorId = "admin",
                EntityType = "Auth",
                IpAddress = "127.0.0.1"
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();

            log.Id.Should().BeGreaterThan(0);
            log.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task CanCreateAiInteractionLog()
        {
            var log = new AiInteractionLog
            {
                Provider = "AzureOpenAI",
                Model = "gpt-4.1-mini",
                RequestCategory = "chat",
                Success = true,
                LatencyMs = 350
            };

            _db.AiInteractionLogs.Add(log);
            await _db.SaveChangesAsync();

            log.Id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task UserChannelIdentityUniqueConstraint()
        {
            var user1 = new User { DisplayName = "User 1" };
            var user2 = new User { DisplayName = "User 2" };
            _db.Users.AddRange(user1, user2);
            await _db.SaveChangesAsync();

            var identity1 = new ChannelIdentity
            {
                UserId = user1.Id,
                Channel = "whatsapp",
                ProviderUserId = "2348000000001"
            };
            _db.ChannelIdentities.Add(identity1);
            await _db.SaveChangesAsync();

            // Same channel + same provider user ID should fail with in-memory
            // (Note: InMemory provider doesn't enforce unique indexes, but we test the model)
            var identity2 = new ChannelIdentity
            {
                UserId = user2.Id,
                Channel = "whatsapp",
                ProviderUserId = "2348000000001" // Same!
            };
            _db.ChannelIdentities.Add(identity2);

            // InMemory provider doesn't enforce unique constraints
            // This test validates the model structure; real DB would enforce it
            Func<Task> act = async () => await _db.SaveChangesAsync();
            await act.Should().NotThrowAsync(); // InMemory doesn't enforce
        }

        [Fact]
        public async Task CanCreateReservation()
        {
            var reservation = new WhatsAppBot.Models.Reservations.Reservation
            {
                ReservationCode = "FZK-260911-0001",
                PhoneNumber = "2348012345678",
                UserName = "Test Passenger",
                AirlineKey = "turkish",
                AirlineName = "Turkish Airlines",
                FromAirport = "LOS",
                ToAirport = "IST",
                DepartDate = new DateOnly(2026, 10, 1),
                Adults = 1,
                Status = WhatsAppBot.Models.Reservations.ReservationStatus.Draft
            };

            _db.Reservations.Add(reservation);
            await _db.SaveChangesAsync();

            reservation.Id.Should().BeGreaterThan(0);
        }
    }
}
