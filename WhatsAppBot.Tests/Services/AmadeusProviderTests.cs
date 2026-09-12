using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Flights;
using WhatsAppBot.Services.Scraping;
using Xunit;

namespace WhatsAppBot.Tests.Services
{
    /// <summary>
    /// Amadeus pricing provider failure handling: the platform must degrade
    /// gracefully when the external API fails (auth errors, HTTP errors,
    /// malformed responses) — never crashing, never fabricating prices.
    /// </summary>
    public class AmadeusProviderTests
    {
        private static readonly AirlineTarget TestAirline = new()
        {
            Name = "Turkish Airlines",
            SourceKey = "turkish",
            StartUrl = "https://www.turkishairlines.com",
            AllowedHost = "www.turkishairlines.com"
        };

        private static readonly FlightSearchDraft TestDraft = new()
        {
            SourceKey = "turkish",
            From = "LOS",
            To = "IST",
            IsRoundTrip = false,
            DepartDate = new DateOnly(2026, 12, 1),
            Adults = 1,
            Children = 0,
            Infants = 0
        };

        private static AmadeusFlightPricingProvider CreateProvider(
            HttpMessageHandler handler, bool enabled = true)
        {
            var options = new AmadeusOptions
            {
                Enabled = enabled,
                BaseUrl = "https://test.api.amadeus.com",
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret"
            };

            return new AmadeusFlightPricingProvider(
                new HttpClient(handler),
                Options.Create(options),
                new Mock<ILogger<AmadeusFlightPricingProvider>>().Object);
        }

        private sealed class FakeHttpHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _responders = new();

            public void Enqueue(HttpStatusCode status, string body) =>
                _responders.Enqueue(_ => Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                }));

            public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
                _responders.Enqueue(req => Task.FromResult(responder(req)));

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (_responders.Count == 0)
                    throw new InvalidOperationException("No queued response");
                var next = _responders.Dequeue();
                return next(request);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task DisabledProvider_ReturnsNull()
        {
            var provider = CreateProvider(new FakeHttpHandler(), enabled: false);

            var quote = await provider.TryQuoteAsync(TestAirline, TestDraft, CancellationToken.None);

            quote.Should().BeNull();
        }

        [Fact]
        public async Task MissingCredentials_ReturnsNull()
        {
            var options = new AmadeusOptions
            {
                Enabled = true,
                BaseUrl = "https://test.api.amadeus.com",
                ClientId = null,
                ClientSecret = null
            };
            var provider = new AmadeusFlightPricingProvider(
                new HttpClient(new FakeHttpHandler()),
                Options.Create(options),
                new Mock<ILogger<AmadeusFlightPricingProvider>>().Object);

            var quote = await provider.TryQuoteAsync(TestAirline, TestDraft, CancellationToken.None);

            quote.Should().BeNull();
        }

        [Fact]
        public async Task OAuth401_ReturnsNull_Gracefully()
        {
            var handler = new FakeHttpHandler();
            handler.Enqueue(HttpStatusCode.Unauthorized,
                "{\"error\":\"invalid_client\"}");
            var provider = CreateProvider(handler);

            var quote = await provider.TryQuoteAsync(TestAirline, TestDraft, CancellationToken.None);

            quote.Should().BeNull("an auth failure must degrade gracefully, not throw");
        }

        [Fact]
        public async Task OAuth500_ReturnsNull_Gracefully()
        {
            var handler = new FakeHttpHandler();
            handler.Enqueue(HttpStatusCode.InternalServerError, "boom");
            var provider = CreateProvider(handler);

            var quote = await provider.TryQuoteAsync(TestAirline, TestDraft, CancellationToken.None);

            quote.Should().BeNull();
        }

        [Fact]
        public async Task Offers429_ReturnsNull_Gracefully()
        {
            var handler = new FakeHttpHandler();
            handler.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"tok\"}");
            handler.Enqueue(HttpStatusCode.TooManyRequests, "{\"errors\":[{\"code\":\"throttled\"}]}");
            var provider = CreateProvider(handler);

            var quote = await provider.TryQuoteAsync(TestAirline, TestDraft, CancellationToken.None);

            quote.Should().BeNull();
        }

        [Fact]
        public async Task OffersEmptyData_ReturnsNull()
        {
            var handler = new FakeHttpHandler();
            handler.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"tok\"}");
            handler.Enqueue(HttpStatusCode.OK, "{\"data\":[]}");
            var provider = CreateProvider(handler);

            var quote = await provider.TryQuoteAsync(TestAirline, TestDraft, CancellationToken.None);

            quote.Should().BeNull("no offers → no fabricated price");
        }

        [Fact]
        public async Task SuccessfulOffers_ReturnsCheapestQuote()
        {
            var handler = new FakeHttpHandler();
            handler.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"tok\"}");
            handler.Enqueue(HttpStatusCode.OK, """
                {
                  "data": [
                    { "price": { "total": "1250.50", "currency": "USD" } },
                    { "price": { "total": "990.00", "currency": "USD" } },
                    { "price": { "total": "1400.00", "currency": "USD" } }
                  ]
                }
                """);
            var provider = CreateProvider(handler);

            var quote = await provider.TryQuoteAsync(TestAirline, TestDraft, CancellationToken.None);

            quote.Should().NotBeNull();
            quote!.Price.Should().Be(990.00m);
            quote.Currency.Should().Be("USD");
            quote.IsPriceExact.Should().BeTrue();
            quote.SourceKey.Should().Be("turkish");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SERVICE-LEVEL: provider exceptions must not kill the pricing pipeline
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task PricingService_AmalgamatesFailingProvider_WithFallback()
        {
            // A provider that throws (e.g., malformed JSON from Amadeus) must
            // be skipped; the final fallback (booking deep link) is returned.
            var throwing = new ThrowingProvider();
            var deepLink = new DeepLinkFlightPricingProvider();

            var service = new FlightPricingService(
                new IFlightPricingProvider[] { throwing, deepLink },
                Options.Create(new FlightPricingOptions
                {
                    EnableAmadeus = true,
                    EnableDeepLinkFallback = true,
                    EnableScrapeFallback = false
                }),
                new Mock<ILogger<FlightPricingService>>().Object);

            var quote = await service.GetQuoteAsync(TestAirline, TestDraft,
                FlightPricingMode.Auto, CancellationToken.None);

            quote.Should().NotBeNull("the pipeline must never throw on provider failure");
            quote!.IsPriceExact.Should().BeFalse("a fallback is an estimate, never claimed exact");
            quote.Message.Should().NotBeNullOrEmpty();
        }

        private sealed class ThrowingProvider : IFlightPricingProvider
        {
            public string Name => "Amadeus";
            public Task<FlightQuote?> TryQuoteAsync(AirlineTarget airline, FlightSearchDraft req, CancellationToken ct)
                => throw new JsonException("simulated malformed provider response");
        }
    }
}
