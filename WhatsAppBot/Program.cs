using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using WhatsAppBot.Data;
using WhatsAppBot.Extensions;
using WhatsAppBot.Services;
using WhatsAppBot.Services.Automation;
using WhatsAppBot.Services.CaptchaSolver;
using WhatsAppBot.Services.Flights;
using WhatsAppBot.Services.Flights.Automations;
using WhatsAppBot.Services.Implementations;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Learning;
using WhatsAppBot.Services.Media;
using WhatsAppBot.Services.Reservations;
using WhatsAppBot.Services.Scraping;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Scraping;

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
    Log.Information("  FEMZYK ENTERPRISES - Multi-Channel AI Service Platform v3.0  ");
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

        // Telegram
        ["Telegram:BotToken"] = Env("TELEGRAM_BOT_TOKEN") ?? builder.Configuration["Telegram:BotToken"],
        ["Telegram:WebhookSecret"] = Env("TELEGRAM_WEBHOOK_SECRET") ?? builder.Configuration["Telegram:WebhookSecret"],

        // Azure OpenAI
        ["AzureOpenAI:Endpoint"] = Env("AZURE_OPENAI_ENDPOINT") ?? builder.Configuration["AzureOpenAI:Endpoint"],
        ["AzureOpenAI:Deployment"] = Env("AZURE_OPENAI_DEPLOYMENT") ?? builder.Configuration["AzureOpenAI:Deployment"],
        ["AzureOpenAI:ApiKey"] = Env("AZURE_OPENAI_API_KEY") ?? builder.Configuration["AzureOpenAI:ApiKey"],
        ["AzureOpenAI:ApiVersion"] = Env("AZURE_OPENAI_API_VERSION") ?? builder.Configuration["AzureOpenAI:ApiVersion"],

        // Amadeus
        ["Amadeus:ClientId"] = Env("AMADEUS_CLIENT_ID") ?? builder.Configuration["Amadeus:ClientId"],
        ["Amadeus:ClientSecret"] = Env("AMADEUS_CLIENT_SECRET") ?? builder.Configuration["Amadeus:ClientSecret"],
        ["Amadeus:BaseUrl"] = Env("AMADEUS_BASE_URL") ?? builder.Configuration["Amadeus:BaseUrl"],

        // LLM
        ["LLM:MaxTokens"] = Env("LLM_MAX_TOKENS") ?? builder.Configuration["LLM:MaxTokens"],
        ["LLM:Temperature"] = Env("LLM_TEMPERATURE") ?? builder.Configuration["LLM:Temperature"],
        ["LLM:MaxHistoryMessages"] = Env("LLM_MAX_HISTORY_MESSAGES") ?? builder.Configuration["LLM:MaxHistoryMessages"],

        // Session (7-day inactivity expiry by default)
        ["Session:TimeoutMinutes"] = Env("SESSION_TIMEOUT_MINUTES") ?? builder.Configuration["Session:TimeoutMinutes"],
        ["Session:CleanupIntervalMinutes"] = Env("SESSION_CLEANUP_INTERVAL_MINUTES") ?? builder.Configuration["Session:CleanupIntervalMinutes"],

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

        // JWT
        ["Jwt:Secret"] = Env("JWT_SECRET") ?? "CHANGE_ME_TO_A_SECURE_RANDOM_STRING_AT_LEAST_32_CHARS",
        ["Jwt:Issuer"] = Env("JWT_ISSUER") ?? "AirlineServiceManagement",
        ["Jwt:Audience"] = Env("JWT_AUDIENCE") ?? "AirlineServiceManagement",
        ["Jwt:ExpiryHours"] = Env("JWT_EXPIRY_HOURS") ?? "24",

        // Admin
        ["Admin:Username"] = Env("ADMIN_USERNAME") ?? "admin",
        ["Admin:Password"] = Env("ADMIN_PASSWORD") ?? string.Empty,
    });

    // ─── Infrastructure ──────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddHttpClient();
    builder.Services.AddMemoryCache();

    // ─── Swagger / OpenAPI ───────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Airline Service Management Platform API",
            Version = "v3.0",
            Description = "Multi-channel AI service request management platform with WhatsApp and Telegram support, " +
                          "flight search/booking, product catalog, and administrative endpoints.",
            Contact = new OpenApiContact
            {
                Name = "FEMZYK ENTERPRISES LTD",
                Email = "support@example.com"
            }
        });

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter your JWT token"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ─── JWT Authentication ──────────────────────────────────────────────────
    // The well-known default in appsettings.json is treated as UNCONFIGURED so
    // a forgotten rotation can never result in a publicly-known signing key.
    const string InsecureDefaultJwtSecret = "CHANGE_ME_TO_A_SECURE_RANDOM_STRING_AT_LEAST_32_CHARS";
    var jwtSecret = builder.Configuration["Jwt:Secret"] ?? string.Empty;
    var jwtConfigured = jwtSecret.Length >= 32 &&
                        !string.Equals(jwtSecret, InsecureDefaultJwtSecret, StringComparison.Ordinal);

    if (jwtConfigured)
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
                };
            });
        builder.Services.AddAuthorization();
    }
    else
    {
        // Fail safe: authentication disabled → every [Authorize] endpoint returns 401.
        Log.Warning("JWT secret is missing, too short, or still the default placeholder. " +
                    "Admin authentication is DISABLED until a strong (32+ char) JWT_SECRET is configured.");
        builder.Services.AddAuthorization();
    }

    // ─── Database ────────────────────────────────────────────────────────────
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
                  ?? "Data Source=whatsappbot.db";
    builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connStr));

    // ─── Scraping configuration ───────────────────────────────────────────────
    var scrapingSection = builder.Configuration.GetSection("Scraping");
    builder.Services.Configure<ScrapingOptions>(scrapingSection);

    // ═════════════════════════════════════════════════════════════════════════
    // CORE SERVICES (Multi-Channel Platform)
    // ═════════════════════════════════════════════════════════════════════════

    // ── User Management ──────────────────────────────────────────────────────
    builder.Services.AddScoped<IUserService, UserService>();

    // ── Persistent Session Management ────────────────────────────────────────
    builder.Services.AddScoped<IPersistentSessionService, PersistentSessionService>();

    // ── Conversation / Message Management ────────────────────────────────────
    builder.Services.AddScoped<IConversationService, ConversationService>();

    // ── Service Request Management ───────────────────────────────────────────
    builder.Services.AddScoped<IServiceRequestService, ServiceRequestService>();

    // ── Audit Logging ────────────────────────────────────────────────────────
    builder.Services.AddScoped<IAuditService, AuditService>();

    // ═════════════════════════════════════════════════════════════════════════
    // CHANNEL SERVICES
    // ═════════════════════════════════════════════════════════════════════════

    // ── WhatsApp (Meta Cloud API) ────────────────────────────────────────────
    builder.Services.AddSingleton<IWhatsAppService, MetaWhatsAppService>();

    // ── Telegram ─────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<ITelegramService, TelegramService>();

    // ═════════════════════════════════════════════════════════════════════════
    // BUSINESS SERVICES
    // ═════════════════════════════════════════════════════════════════════════

    // ── LLM / AI Service (wrapped with resilience + interaction logging) ─────
    builder.Services.AddScoped<AzureOpenAiService>();
    builder.Services.AddScoped<ILLMService>(sp =>
    {
        var inner = sp.GetRequiredService<AzureOpenAiService>();
        var logger = sp.GetRequiredService<ILogger<ResilientLlmService>>();
        var db = sp.GetRequiredService<AppDbContext>();
        return new ResilientLlmService(inner, logger, db);
    });

    // ── Shared intent routing + flight conversation (multi-channel) ──────────
    builder.Services.AddScoped<IIntentRouter, IntentRouter>();
    builder.Services.AddScoped<FlightConversationService>();

    builder.Services.AddScoped<IChatLogService, ChatLogService>();
    builder.Services.AddScoped<IProductCatalogService, ProductCatalogService>();
    builder.Services.AddScoped<ICatalogSyncService, CatalogSyncService>();

    // ── CAPTCHA & Automation ─────────────────────────────────────────────────
    builder.Services.AddScoped<ICaptchaService, CaptchaService>();
    builder.Services.AddSingleton<SelfCaptchaSolver>();
    builder.Services.AddSingleton<ProxyManager>();
    builder.Services.AddSingleton<StealthBrowserManager>();
    builder.Services.AddSingleton<BookingOrchestrator>();

    // ── Reservation & Payment ────────────────────────────────────────────────
    builder.Services.AddScoped<IReservationService, ReservationService>();
    builder.Services.AddScoped<WhatsAppBot.Services.Payments.IPaymentService, WhatsAppBot.Services.Payments.StripePaymentService>();

    // ── Per-airline automation classes ────────────────────────────────────────
    builder.Services.AddSingleton<IFlightBookingAutomation, TurkishAirlinesAutomation>();
    builder.Services.AddSingleton<IFlightBookingAutomation, LufthansaAutomation>();
    builder.Services.AddSingleton<IFlightBookingAutomation, AirPeaceAutomation>();
    builder.Services.AddSingleton<IFlightBookingAutomation, ArikAirAutomation>();

    // ── Background workers ───────────────────────────────────────────────────
    builder.Services.AddHostedService<SessionCleanupService>();
    builder.Services.AddHostedService<CatalogSyncHostedService>();

    // ── Flight pricing ───────────────────────────────────────────────────────
    builder.Services.Configure<FlightPricingOptions>(builder.Configuration.GetSection("FlightPricing"));
    builder.Services.Configure<AmadeusOptions>(builder.Configuration.GetSection("Amadeus"));
    builder.Services.AddScoped<FlightPricingService>();
    builder.Services.AddHttpClient<AmadeusFlightPricingProvider>();
    builder.Services.AddScoped<IFlightPricingProvider, AmadeusFlightPricingProvider>();
    builder.Services.AddScoped<IFlightPricingProvider, DeepLinkFlightPricingProvider>();

    // ── Learning & Knowledge System ──────────────────────────────────────────
    builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
    builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();

    // ── Media Services ───────────────────────────────────────────────────────
    builder.Services.AddScoped<IImageGenerationService, ImageGenerationService>();
    builder.Services.AddScoped<IVoiceService, VoiceService>();
    builder.Services.AddScoped<IVisionService, VisionService>();

    // ── Airline scrapers ─────────────────────────────────────────────────────
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
            // One config-driven scraper per airline target. When the target has
            // no CSS selectors configured, the scraper safely returns 0 items
            // (it never fabricates data).
            var target = airline;
            builder.Services.AddScoped<IProductScraper>(sp =>
            {
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
                var logger = sp.GetRequiredService<ILogger<ConfigDrivenHtmlProductScraper>>();
                var catalogOptions = new ScrapeCatalogOptions
                {
                    Enabled = scrapingOpts.Enabled,
                    CatalogUrl = target.StartUrl,
                    SourceName = target.SourceKey,
                    UserAgent = scrapingOpts.UserAgent,
                    HttpTimeoutSeconds = scrapingOpts.RequestTimeoutSeconds,
                    RequestDelayMs = scrapingOpts.DelayBetweenRequestsMs,
                    ProductCardSelector = target.ProductCardSelector ?? string.Empty,
                    NameSelector = target.NameSelector ?? string.Empty,
                    PriceSelector = target.PriceSelector ?? string.Empty,
                    LinkSelector = target.LinkSelector,
                    ImageSelector = target.ImageSelector
                };
                return new ConfigDrivenHtmlProductScraper(http, catalogOptions, logger);
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

    // ─── Middleware ───────────────────────────────────────────────────────────
    app.UseSerilogRequestLogging();

    // Swagger in development
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Airline Service Management API v3.0");
            c.RoutePrefix = "swagger";
        });
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    // ─── Health endpoints ────────────────────────────────────────────────────
    // /health        → liveness: the process is up (no external deps required)
    // /health/ready  → readiness: core dependency (database) is reachable
    // External providers (WhatsApp/Telegram/Azure OpenAI/Amadeus) are optional
    // and intentionally do NOT gate these endpoints.
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "Healthy",
        service = "Airline Service Management Platform",
        version = "3.0.0",
        timestamp = DateTime.UtcNow
    }));

    app.MapGet("/health/ready", async (AppDbContext db) =>
    {
        bool dbOk = false;
        try
        {
            dbOk = await db.Database.CanConnectAsync();
        }
        catch
        {
            dbOk = false;
        }

        return dbOk
            ? Results.Ok(new { status = "Ready", database = "Connected", version = "3.0.0", timestamp = DateTime.UtcNow })
            : Results.Json(new { status = "NotReady", database = "Unavailable", version = "3.0.0", timestamp = DateTime.UtcNow },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    });

    // ─── Service banner ──────────────────────────────────────────────────────
    app.MapGet("/", () => new
    {
        service = "FEMZYK ENTERPRISES - Multi-Channel AI Service Platform",
        version = "3.0",
        status = "Running",
        timestamp = DateTime.UtcNow,
        channels = new[] { "WhatsApp", "Telegram" },
        features = new[]
        {
            "Multi-Channel Messaging (WhatsApp + Telegram)",
            "AI Chat (Azure OpenAI with fallback)",
            "Flight Search (Amadeus + Scraping)",
            "Flight Booking (API + Automation)",
            "Reservation Management",
            "Service Request Management",
            "Product Catalog",
            "Persistent Sessions (7-day expiry)",
            "User Management",
            "Admin API (JWT authenticated)",
            "Audit Logging"
        },
        endpoints = new
        {
            webhook_whatsapp = "/webhook",
            webhook_telegram = "/telegram",
            admin = "/api/admin",
            auth = "/api/auth/login",
            health = "/health",
            readiness = "/health/ready",
            admin_health = "/api/admin/health",
            swagger = "/swagger"
        }
    });

    Log.Information("Bot Name    : {Name}", builder.Configuration["Bot:Name"]);
    Log.Information("Company     : {Company}", builder.Configuration["Bot:Company"]);
    Log.Information("Webhook WA  : http://localhost:5260/webhook");
    Log.Information("Webhook TG  : http://localhost:5260/telegram");
    Log.Information("Admin API   : http://localhost:5260/api/admin");
    Log.Information("Swagger     : http://localhost:5260/swagger");
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

// Marker class so integration tests can use WebApplicationFactory<Program>.
public partial class Program { }
