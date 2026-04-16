using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using WhatsAppBot.Data;
using WhatsAppBot.Extensions;
using WhatsAppBot.Services;
using WhatsAppBot.Services.Automation;
using WhatsAppBot.Services.CaptchaSolver;
using WhatsAppBot.Services.Implementations;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Reservations;
using WhatsAppBot.Services.Scraping;
using WhatsAppBot.Services.Flights;
using WhatsAppBot.Services.Flights.Automations;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Learning;
using WhatsAppBot.Services.Media;

// ─── Load .env FIRST before anything else ───────────────────────────────────
ConfigurationExtensions.LoadDotEnv();

// ─── Serilog bootstrap ───────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/whatsapp-bot-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("===========================================");
    Log.Information("  FEMZYK ENTERPRISES - Travel Bot v2.0   ");
    Log.Information("===========================================");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // ─── Merge env vars into configuration ───────────────────────────────────
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        // WhatsApp
        ["MetaWhatsApp:GraphBase"] = Env("WHATSAPP_GRAPH_BASE") ?? builder.Configuration["MetaWhatsApp:GraphBase"],
        ["MetaWhatsApp:ApiVersion"] = Env("WHATSAPP_API_VERSION") ?? builder.Configuration["MetaWhatsApp:ApiVersion"],
        ["MetaWhatsApp:PhoneNumberId"] = Env("WHATSAPP_PHONE_NUMBER_ID") ?? builder.Configuration["MetaWhatsApp:PhoneNumberId"],
        ["MetaWhatsApp:AccessToken"] = Env("WHATSAPP_ACCESS_TOKEN") ?? builder.Configuration["MetaWhatsApp:AccessToken"],
        ["MetaWhatsApp:VerifyToken"] = Env("WHATSAPP_VERIFY_TOKEN") ?? builder.Configuration["MetaWhatsApp:VerifyToken"],
        ["MetaWhatsApp:AppSecret"] = Env("WHATSAPP_APP_SECRET") ?? builder.Configuration["MetaWhatsApp:AppSecret"],

        // Azure OpenAI
        ["AzureOpenAI:Endpoint"] = Env("AZURE_OPENAI_ENDPOINT") ?? builder.Configuration["AzureOpenAI:Endpoint"],
        ["AzureOpenAI:Deployment"] = Env("AZURE_OPENAI_DEPLOYMENT") ?? builder.Configuration["AzureOpenAI:Deployment"],
        ["AzureOpenAI:ApiKey"] = Env("AZURE_OPENAI_API_KEY") ?? builder.Configuration["AzureOpenAI:ApiKey"],
        ["AzureOpenAI:ApiVersion"] = Env("AZURE_OPENAI_API_VERSION") ?? builder.Configuration["AzureOpenAI:ApiVersion"],

        // Amadeus
        ["FlightPricing:AmadeusClientId"] = Env("AMADEUS_CLIENT_ID") ?? builder.Configuration["FlightPricing:AmadeusClientId"],
        ["FlightPricing:AmadeusClientSecret"] = Env("AMADEUS_CLIENT_SECRET") ?? builder.Configuration["FlightPricing:AmadeusClientSecret"],
        ["FlightPricing:AmadeusBaseUrl"] = Env("AMADEUS_BASE_URL") ?? builder.Configuration["FlightPricing:AmadeusBaseUrl"],

        // CAPTCHA
        ["CaptchaService:ApiKey"] = Env("CAPTCHA_API_KEY") ?? string.Empty,
        ["CaptchaService:Provider"] = Env("CAPTCHA_SERVICE_PROVIDER") ?? "self",

        // Stripe
        ["Stripe:SecretKey"] = Env("STRIPE_SECRET_KEY") ?? string.Empty,
        ["Stripe:PublishableKey"] = Env("STRIPE_PUBLISHABLE_KEY") ?? string.Empty,
        ["Stripe:WebhookSecret"] = Env("STRIPE_WEBHOOK_SECRET") ?? string.Empty,

        // Azure Speech
        ["AzureSpeech:Key"] = Env("AZURE_SPEECH_KEY") ?? string.Empty,
        ["AzureSpeech:Region"] = Env("AZURE_SPEECH_REGION") ?? "eastus",

        // DB
        ["ConnectionStrings:DefaultConnection"] = Env("DATABASE_CONNECTION_STRING") ?? "Data Source=whatsappbot.db",

        // Bot personality
        ["Bot:Name"] = Env("BOT_NAME") ?? "Femzyk_Aje_Bot",
        ["Bot:Company"] = Env("BOT_COMPANY") ?? "FEMZYK ENTERPRISES LTD",
        ["Bot:SupportEmail"] = Env("BOT_SUPPORT_EMAIL") ?? "femzykenterprisesltd@gmail.com",
    });

    // ─── Infrastructure ──────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddHttpClient();
    builder.Services.AddMemoryCache();

    // ─── Database ────────────────────────────────────────────────────────────
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
                  ?? "Data Source=whatsappbot.db";
    builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connStr));

    // ─── Scraping configuration ───────────────────────────────────────────────
    var scrapingSection = builder.Configuration.GetSection("Scraping");
    builder.Services.Configure<ScrapingOptions>(scrapingSection);

    // ─── Core bot services ───────────────────────────────────────────────────
    builder.Services.AddSingleton<ISessionService, InMemorySessionService>();
    builder.Services.AddScoped<ILLMService, AzureOpenAiService>();
    builder.Services.AddSingleton<IWhatsAppService, MetaWhatsAppService>();
    builder.Services.AddScoped<IChatLogService, ChatLogService>();
    builder.Services.AddScoped<IProductCatalogService, ProductCatalogService>();
    builder.Services.AddScoped<ICatalogSyncService, CatalogSyncService>();

    // ─── CAPTCHA & Automation ─────────────────────────────────────────────────
    builder.Services.AddScoped<ICaptchaService, CaptchaService>();
    builder.Services.AddSingleton<SelfCaptchaSolver>();
    builder.Services.AddSingleton<ProxyManager>();
    builder.Services.AddSingleton<StealthBrowserManager>();
    builder.Services.AddSingleton<BookingOrchestrator>();

    // ─── Reservation & Payment ────────────────────────────────────────────────
    builder.Services.AddScoped<IReservationService, ReservationService>();

    // ─── Per-airline automation classes ───────────────────────────────────────
    builder.Services.AddSingleton<IFlightBookingAutomation, TurkishAirlinesAutomation>();
    builder.Services.AddSingleton<IFlightBookingAutomation, LufthansaAutomation>();
    builder.Services.AddSingleton<IFlightBookingAutomation, AirPeaceAutomation>();
    builder.Services.AddSingleton<IFlightBookingAutomation, ArikAirAutomation>();

    // ─── Background workers ───────────────────────────────────────────────────
    builder.Services.AddHostedService<SessionCleanupService>();
    builder.Services.AddHostedService<CatalogSyncHostedService>();

    // ─── Flight pricing ───────────────────────────────────────────────────────
    builder.Services.Configure<FlightPricingOptions>(builder.Configuration.GetSection("FlightPricing"));
    builder.Services.Configure<AmadeusOptions>(builder.Configuration.GetSection("Amadeus"));
    builder.Services.AddScoped<FlightPricingService>();
    builder.Services.AddHttpClient<AmadeusFlightPricingProvider>();
    builder.Services.AddScoped<IFlightPricingProvider, AmadeusFlightPricingProvider>();
    builder.Services.AddScoped<IFlightPricingProvider, DeepLinkFlightPricingProvider>();

    // ─── Learning & Knowledge System ──────────────────────────────────────────
    builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
    builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();

    // ─── Media Services (Image, Voice, Vision) ───────────────────────────────────
    builder.Services.AddScoped<IImageGenerationService, ImageGenerationService>();
    builder.Services.AddScoped<IVoiceService, VoiceService>();
    builder.Services.AddScoped<IVisionService, VisionService>();

    // ─── Airline scrapers (one per configured airline) ────────────────────────
    var scrapingOpts = scrapingSection.Get<ScrapingOptions>() ?? new ScrapingOptions();
    var airlines = scrapingOpts.Airlines ?? new List<AirlineTarget>();

    if (airlines.Count == 0)
    {
        Log.Warning("No airlines configured under Scraping:Airlines.");
    }
    else
    {
        foreach (var airline in airlines)
        {
            builder.Services.AddScoped<IProductScraper>(sp =>
            {
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
                var logger = sp.GetRequiredService<ILogger<AirlineProductScraper>>();
                var options = sp.GetRequiredService<IOptions<ScrapingOptions>>();
                return new AirlineProductScraper(http, logger, options, airline);
            });
        }
        Log.Information("Registered {Count} airline scrapers", airlines.Count);
    }

    // ─── Build app ────────────────────────────────────────────────────────────
    var app = builder.Build();

    // Ensure DB schema is created
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        Log.Information("Database ready: {ConnStr}", connStr);
    }

    app.UseSerilogRequestLogging();
    app.MapControllers();

    // Health endpoint
    app.MapGet("/", () => new
    {
        service = "FEMZYK ENTERPRISES - WhatsApp Travel Bot",
        version = "2.0",
        status = "Running",
        timestamp = DateTime.UtcNow,
        features = new[]
        {
            "AI Chat (Azure OpenAI)",
            "Flight Search (Amadeus + Scraping)",
            "Flight Booking (API + Automation)",
            "Reservation Management",
            "Cancellation Engine",
            "CAPTCHA Bypass (Self-hosted)",
            "IP Rotation",
            "Product Catalog"
        },
        endpoints = new
        {
            webhook = "/webhook",
            alt_webhook = "/api/webhook"
        }
    });

    Log.Information("Bot Name    : {Name}", builder.Configuration["Bot:Name"]);
    Log.Information("Company     : {Company}", builder.Configuration["Bot:Company"]);
    Log.Information("Webhook     : http://localhost:5260/webhook");
    Log.Information("Alt Webhook : http://localhost:5260/api/webhook");
    Log.Information("===========================================");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// ─── Helper ──────────────────────────────────────────────────────────────────
static string? Env(string key) =>
    Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : null;