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
using WhatsAppBot.Services.Implementations;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Scraping;
using WhatsAppBot.Services.Flights;
using WhatsAppBot.Models.Flights;

// Load .env file BEFORE any configuration
ConfigurationExtensions.LoadDotEnv();

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
    Log.Information("Starting WhatsApp Bot...");
    Log.Information("===========================================");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Override appsettings with environment variables
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["MetaWhatsApp:GraphBase"] = Environment.GetEnvironmentVariable("WHATSAPP_GRAPH_BASE") ?? builder.Configuration["MetaWhatsApp:GraphBase"],
        ["MetaWhatsApp:ApiVersion"] = Environment.GetEnvironmentVariable("WHATSAPP_API_VERSION") ?? builder.Configuration["MetaWhatsApp:ApiVersion"],
        ["MetaWhatsApp:PhoneNumberId"] = Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID") ?? builder.Configuration["MetaWhatsApp:PhoneNumberId"],
        ["MetaWhatsApp:AccessToken"] = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN") ?? builder.Configuration["MetaWhatsApp:AccessToken"],
        ["MetaWhatsApp:VerifyToken"] = Environment.GetEnvironmentVariable("WHATSAPP_VERIFY_TOKEN") ?? builder.Configuration["MetaWhatsApp:VerifyToken"],
        ["MetaWhatsApp:AppSecret"] = Environment.GetEnvironmentVariable("WHATSAPP_APP_SECRET") ?? builder.Configuration["MetaWhatsApp:AppSecret"],
        
        ["AzureOpenAI:Endpoint"] = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? builder.Configuration["AzureOpenAI:Endpoint"],
        ["AzureOpenAI:Deployment"] = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? builder.Configuration["AzureOpenAI:Deployment"],
        ["AzureOpenAI:ApiKey"] = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? builder.Configuration["AzureOpenAI:ApiKey"],
        ["AzureOpenAI:ApiVersion"] = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? builder.Configuration["AzureOpenAI:ApiVersion"],
        
        ["FlightPricing:AmadeusClientId"] = Environment.GetEnvironmentVariable("AMADEUS_CLIENT_ID") ?? builder.Configuration["FlightPricing:AmadeusClientId"],
        ["FlightPricing:AmadeusClientSecret"] = Environment.GetEnvironmentVariable("AMADEUS_CLIENT_SECRET") ?? builder.Configuration["FlightPricing:AmadeusClientSecret"],
        
        ["CaptchaService:ApiKey"] = Environment.GetEnvironmentVariable("CAPTCHA_API_KEY") ?? string.Empty,
        ["CaptchaService:Provider"] = Environment.GetEnvironmentVariable("CAPTCHA_SERVICE_PROVIDER") ?? "2captcha",
        
        ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING") ?? "Data Source=whatsappbot.db"
    });

    builder.Services.AddControllers();

    // HttpClientFactory (Meta sending + external calls)
    builder.Services.AddHttpClient();
    builder.Services.AddMemoryCache();

    // DB
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=whatsappbot.db";
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(connectionString));

    // Scraping config
    var scrapingSection = builder.Configuration.GetSection("Scraping");
    builder.Services.Configure<ScrapingOptions>(scrapingSection);

    // Core bot services
    builder.Services.AddSingleton<ISessionService, InMemorySessionService>();
    builder.Services.AddScoped<ILLMService, AzureOpenAiService>();
    builder.Services.AddSingleton<IWhatsAppService, MetaWhatsAppService>();
    builder.Services.AddScoped<IChatLogService, ChatLogService>();

    // Catalog DB query services
    builder.Services.AddScoped<IProductCatalogService, ProductCatalogService>();

    // Sync engine
    builder.Services.AddScoped<ICatalogSyncService, CatalogSyncService>();

    // CAPTCHA Service
    builder.Services.AddScoped<ICaptchaService, CaptchaService>();

    // Background workers
    builder.Services.AddHostedService<SessionCleanupService>();
    builder.Services.AddHostedService<CatalogSyncHostedService>();

    // ===== Flight Pricing =====
    builder.Services.Configure<FlightPricingOptions>(builder.Configuration.GetSection("FlightPricing"));
    builder.Services.Configure<AmadeusOptions>(builder.Configuration.GetSection("Amadeus"));

    builder.Services.AddScoped<FlightPricingService>();

    // Self-hosted CAPTCHA solver (FREE!)
    builder.Services.AddSingleton<SelfCaptchaSolver>();

    builder.Services.AddHttpClient<AmadeusFlightPricingProvider>();
    builder.Services.AddScoped<IFlightPricingProvider, AmadeusFlightPricingProvider>();

    builder.Services.AddScoped<IFlightPricingProvider, DeepLinkFlightPricingProvider>();

    // ===== Register one scraper per airline =====
    var scrapingOptPreview = scrapingSection.Get<ScrapingOptions>() ?? new ScrapingOptions();
    var airlines = scrapingOptPreview.Airlines ?? new List<AirlineTarget>();

    if (airlines.Count == 0)
    {
        Log.Warning("No airlines configured under Scraping:Airlines. Scraping will not run.");
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
    }

    var app = builder.Build();

    // Ensure DB created (prototype mode)
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        Log.Information("Database initialized successfully");
    }

    app.UseSerilogRequestLogging();
    app.MapControllers();

    app.MapGet("/", () => new
    {
        service = "WhatsApp Bot",
        status = "Running",
        timestamp = DateTime.UtcNow,
        version = "2.0",
        endpoints = new
        {
            webhook = "/webhook",
            alt_webhook = "/api/webhook"
        }
    });

    Log.Information("WhatsApp Bot is ready!");
    Log.Information("Webhook URL: http://localhost:5260/webhook");
    Log.Information("Alt Webhook URL: http://localhost:5260/api/webhook");
    Log.Information("Environment: {Env}", builder.Environment.EnvironmentName);
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