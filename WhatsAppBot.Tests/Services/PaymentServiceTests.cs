using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using WhatsAppBot.Data;
using WhatsAppBot.Models.Payments;
using WhatsAppBot.Services.Payments;
using Xunit;

namespace WhatsAppBot.Tests.Services
{
    /// <summary>
    /// Payment service safety: the platform must NEVER fabricate a successful
    /// payment. When no provider is configured, operations fail cleanly with
    /// auditable records.
    /// </summary>
    public class PaymentServiceTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly StripePaymentService _service;

        public PaymentServiceTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            // No STRIPE_SECRET_KEY configured (CI never has real provider creds)
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["Stripe:SecretKey"] = null
                })
                .Build();

            _service = new StripePaymentService(_db, config, new Mock<ILogger<StripePaymentService>>().Object);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public void IsConfigured_False_WhenNoProviderCredentials()
        {
            _service.IsConfigured.Should().BeFalse();
            _service.ProviderName.Should().Be("none");
        }

        [Fact]
        public async Task CreatePayment_Unconfigured_ReturnsCleanFailure_NeverSuccess()
        {
            var result = await _service.CreatePaymentAsync(
                "FZK-260912-0001", "2348012345678", 150000m, "NGN");

            result.Success.Should().BeFalse("no real payment can succeed without a provider");
            result.Status.Should().Be(PaymentStatus.Cancelled);
            result.Reason.Should().Contain("not configured");
            result.Simulated.Should().BeFalse();
            result.ExternalPaymentId.Should().BeNull();
        }

        [Fact]
        public async Task CreatePayment_Unconfigured_PersistsAuditableRecord()
        {
            await _service.CreatePaymentAsync(
                "FZK-260912-0002", "2348012345678", 150000m, "NGN");

            var record = await _service.GetByReservationCodeAsync("FZK-260912-0002");

            record.Should().NotBeNull("every attempted payment must be auditable");
            record!.Status.Should().Be(PaymentStatus.Cancelled);
            record.Provider.Should().Be("none");
            record.FailureReason.Should().Contain("not configured");
            record.Amount.Should().Be(150000m);
        }

        [Fact]
        public async Task CreatePayment_NeverSucceedsWithoutProvider()
        {
            // Regression: no code path may mark a payment Succeeded without a
            // real provider response.
            var result = await _service.CreatePaymentAsync(
                "FZK-260912-0003", "2348012345678", 1m, "USD");

            result.Status.Should().NotBe(PaymentStatus.Succeeded);
        }

        [Fact]
        public async Task CreatePayment_InvalidAmount_Throws()
        {
            var act = async () => await _service.CreatePaymentAsync("FZK-1", "234", 0m, "NGN");
            await act.Should().ThrowAsync<ArgumentException>();

            var act2 = async () => await _service.CreatePaymentAsync("FZK-1", "234", -5m, "NGN");
            await act2.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task CreatePayment_MissingReservationCode_Throws()
        {
            var act = async () => await _service.CreatePaymentAsync("", "234", 100m, "NGN");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task RecordProviderEvent_UpdatesStatusAndCompletion()
        {
            var record = new PaymentRecord
            {
                ReservationCode = "FZK-260912-0004",
                PhoneNumber = "2348012345678",
                Amount = 100m,
                Currency = "NGN",
                Provider = "stripe",
                ExternalPaymentId = "pi_test_123",
                Status = PaymentStatus.Pending
            };
            _db.Payments.Add(record);
            await _db.SaveChangesAsync();

            var updated = await _service.RecordProviderEventAsync(
                "pi_test_123", PaymentStatus.Succeeded);

            updated.Should().NotBeNull();
            updated!.Status.Should().Be(PaymentStatus.Succeeded);
            updated.CompletedAtUtc.Should().NotBeNull();
        }

        [Fact]
        public async Task RecordProviderEvent_UnknownId_ReturnsNull()
        {
            var updated = await _service.RecordProviderEventAsync("pi_nonexistent", PaymentStatus.Succeeded);
            updated.Should().BeNull();
        }

        [Fact]
        public async Task RecordProviderEvent_FailedStoresReason()
        {
            var record = new PaymentRecord
            {
                ReservationCode = "FZK-260912-0005",
                PhoneNumber = "2348012345678",
                Amount = 100m,
                Currency = "NGN",
                Provider = "stripe",
                ExternalPaymentId = "pi_test_456",
                Status = PaymentStatus.Pending
            };
            _db.Payments.Add(record);
            await _db.SaveChangesAsync();

            var updated = await _service.RecordProviderEventAsync(
                "pi_test_456", PaymentStatus.Failed, "Card declined by issuer");

            updated!.Status.Should().Be(PaymentStatus.Failed);
            updated.FailureReason.Should().Be("Card declined by issuer");
        }
    }
}
