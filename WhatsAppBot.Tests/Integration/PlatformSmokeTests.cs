using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using WhatsAppBot.Data;
using Xunit;

namespace WhatsAppBot.Tests.Integration
{
    /// <summary>
    /// Runtime smoke tests: the real application host (Program.cs) is started
    /// with mock-free configuration — no external provider credentials.
    ///
    /// These prove what the Docker smoke test in CI proves at the container
    /// level: startup without providers, /health, webhook security, and admin
    /// auth enforcement.
    /// </summary>
    public class PlatformSmokeTests : IClassFixture<PlatformSmokeTests.FactoryOptions>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public PlatformSmokeTests(FactoryOptions options)
        {
            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder
                        .UseEnvironment("Development")
                        .UseSetting("Scraping:Enabled", "false")
                        .UseSetting("Scraping:RunOnStartup", "false")
                        .UseSetting("DATABASE_CONNECTION_STRING", options.DbPath)
                        .UseSetting("MetaWhatsApp:VerifyToken", "test-verify-token")
                        .UseSetting("MetaWhatsApp:AppSecret", "test-app-secret-for-unit-tests")
                        .UseSetting("Telegram:WebhookSecret", "test-telegram-secret")
                        .ConfigureLogging((ctx, logging) => logging.ClearProviders());
                });
        }

        /// <summary>
        /// Shared factory options: a unique temp SQLite file per test class.
        /// The env var is set because Program.cs maps DATABASE_CONNECTION_STRING
        /// (environment) into ConnectionStrings:Default.
        /// </summary>
        public sealed class FactoryOptions
        {
            public string DbPath { get; } =
                Path.Combine(Path.GetTempPath(), $"whatsappbot-tests-{Guid.NewGuid():N}.db");

            public FactoryOptions()
            {
                Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING",
                    $"Data Source={DbPath}");
            }
        }

        private HttpClient CreateClient() => _factory.CreateClient();

        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task App_Boots_WithoutAnyExternalProviderCredentials()
        {
            // The host resolves the full DI graph lazily per request; this
            // exercises startup + a scoped request end-to-end.
            var client = CreateClient();
            var response = await client.GetAsync("/health");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Health_ReturnsHealthyStatus()
        {
            var client = CreateClient();
            var response = await client.GetAsync("/health");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
            doc.RootElement.GetProperty("service").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task HealthReady_ReportsDatabaseConnected()
        {
            var client = CreateClient();
            var response = await client.GetAsync("/health/ready");

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "the platform must be ready with only its own database");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("database").GetString().Should().Be("Connected");
        }

        [Fact]
        public async Task Root_ReturnsServiceBannerWithEndpoints()
        {
            var client = CreateClient();
            var response = await client.GetAsync("/");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var endpoints = doc.RootElement.GetProperty("endpoints");
            endpoints.GetProperty("webhook_whatsapp").GetString().Should().Be("/webhook");
            endpoints.GetProperty("health").GetString().Should().Be("/health");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Webhook security
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task WhatsAppVerify_WrongToken_Returns401()
        {
            var client = CreateClient();
            var response = await client.GetAsync(
                "/webhook?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=x");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task WhatsAppVerify_CorrectToken_ReturnsChallenge()
        {
            var client = CreateClient();
            var response = await client.GetAsync(
                "/webhook?hub.mode=subscribe&hub.verify_token=test-verify-token&hub.challenge=ch-123");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("ch-123");
        }

        [Fact]
        public async Task WhatsAppWebhook_InvalidHmac_Returns401()
        {
            var client = CreateClient();
            var payload = "{\"entry\":[]}";

            using var response = await client.PostAsync(
                "/webhook",
                new StringContent(payload, Encoding.UTF8, "application/json")
                {
                    Headers = { { "X-Hub-Signature-256", "sha256=0000000000000000000000000000000000000000000000000000000000000000" } }
                );

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "a payload with an invalid HMAC must never be processed");
        }

        [Fact]
        public async Task WhatsAppWebhook_ValidHmac_AcceptsPayload()
        {
            var client = CreateClient();
            var payload = "{\"entry\":[]}";
            var signature = Sign(payload, "test-app-secret-for-unit-tests");

            using var response = await client.PostAsync(
                "/webhook",
                new StringContent(payload, Encoding.UTF8, "application/json")
                {
                    Headers = { { "X-Hub-Signature-256", signature } }
                );

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task TelegramWebhook_WrongSecret_Returns401()
        {
            var client = CreateClient();

            using var response = await client.PostAsync(
                "/telegram",
                new StringContent("{}", Encoding.UTF8, "application/json")
                {
                    Headers = { { "X-Telegram-Bot-Api-Secret-Token", "definitely-wrong" } }
                );

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task TelegramWebhook_CorrectSecret_AcceptsPayload()
        {
            var client = CreateClient();

            using var response = await client.PostAsync(
                "/telegram",
                new StringContent("{}", Encoding.UTF8, "application/json")
                {
                    Headers = { { "X-Telegram-Bot-Api-Secret-Token", "test-telegram-secret" } }
                );

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Admin auth enforcement
        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public async Task AdminApi_WithoutToken_Returns401()
        {
            var client = CreateClient();
            var response = await client.GetAsync("/api/admin/dashboard");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task AdminApi_WithGarbageToken_Returns401()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", "eyJhbGciOiJub25lIn0.garbage.token");

            var response = await client.GetAsync("/api/admin/users");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "a malformed token must be rejected");
        }

        [Fact]
        public async Task AuthLogin_WithDefaultPlaceholderJwtSecret_Returns503()
        {
            // appsettings.json ships a well-known placeholder secret. The app
            // must treat it as UNCONFIGURED and refuse to issue tokens.
            var client = CreateClient();

            using var response = await client.PostAsync(
                "/api/auth/login",
                new StringContent(
                    "{\"username\":\"admin\",\"password\":\"whatever\"}",
                    Encoding.UTF8, "application/json"));

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
                "a well-known default JWT secret must never be used for signing");
        }

        // ═══════════════════════════════════════════════════════════════════════
        [Fact]
        public void FullDiGraph_InjectsSuccessfully()
        {
            // Build a scope and resolve the key services to prove the whole
            // container is valid (Development environment validates scopes).
            using var scope = _factory.Services.CreateScope();
            var sp = scope.ServiceProvider;

            sp.GetRequiredService<WhatsAppBot.Services.Interfaces.IWhatsAppService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Interfaces.ITelegramService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Interfaces.ILLMService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Interfaces.IUserService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Interfaces.IPersistentSessionService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Interfaces.IConversationService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Interfaces.IServiceRequestService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Interfaces.IAuditService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Flights.IIntentRouter>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Flights.FlightConversationService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Payments.IPaymentService>()
                .Should().NotBeNull();
            sp.GetRequiredService<WhatsAppBot.Services.Automation.BookingOrchestrator>()
                .Should().NotBeNull();
        }

        private static string Sign(string payload, string secret)
        {
            using var hmac = new System.Security.Cryptography.HMACSHA256(
                Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
