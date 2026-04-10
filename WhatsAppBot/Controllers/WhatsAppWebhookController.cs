using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Models;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Flights;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Controllers
{
    [ApiController]
    [Route("webhook")]
    [Route("api/webhook")]
    public partial class WhatsAppWebhookController : ControllerBase
    {
        private readonly ILLMService _llm;
        private readonly ISessionService _session;
        private readonly IWhatsAppService _whatsApp;
        private readonly IChatLogService _chatLog;
        private readonly IProductCatalogService _catalog;
        private readonly FlightPricingService _flightPricing;
        private readonly ScrapingOptions _scrapingOpt;
        private readonly ILogger<WhatsAppWebhookController> _logger;
        private readonly IConfiguration _config;

        private static readonly string[] GreetingWords =
        [
            "hi","hello","hey","yo","sup","wassup","good morning","good afternoon","good evening",
            "hii","heyy","hola","hi fam","hello fam","hey fam"
        ];

        private static readonly HashSet<string> GreetingSet =
            new(GreetingWords, StringComparer.OrdinalIgnoreCase);

        public WhatsAppWebhookController(
            ILLMService llm,
            ISessionService session,
            IWhatsAppService whatsApp,
            IChatLogService chatLog,
            IProductCatalogService catalog,
            FlightPricingService flightPricing,
            IOptions<ScrapingOptions> scrapingOptions,
            ILogger<WhatsAppWebhookController> logger,
            IConfiguration config)
        {
            _llm = llm;
            _session = session;
            _whatsApp = whatsApp;
            _chatLog = chatLog;
            _catalog = catalog;
            _flightPricing = flightPricing;
            _scrapingOpt = scrapingOptions.Value;
            _logger = logger;
            _config = config;
        }

        [HttpGet]
        public IActionResult Verify(
            [FromQuery(Name = "hub.mode")] string mode,
            [FromQuery(Name = "hub.verify_token")] string token,
            [FromQuery(Name = "hub.challenge")] string challenge)
        {
            var expected = _config["MetaWhatsApp:VerifyToken"];

            if (mode == "subscribe" &&
                !string.IsNullOrWhiteSpace(expected) &&
                string.Equals(token, expected, StringComparison.Ordinal))
            {
                _logger.LogInformation("Meta webhook verified successfully.");
                return Content(challenge ?? string.Empty, "text/plain");
            }

            _logger.LogWarning("Meta webhook verification failed.");
            return Unauthorized();
        }

        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            try
            {
                Request.EnableBuffering();

                string rawBody;
                using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
                {
                    rawBody = await reader.ReadToEndAsync();
                    Request.Body.Position = 0;
                }

                if (!VerifySignature(rawBody))
                {
                    _logger.LogWarning("Invalid webhook signature.");
                    return Unauthorized();
                }

                if (!TryExtractIncomingMessage(rawBody, out var fromNumber, out var messageText))
                    return Ok();

                messageText = (messageText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(messageText))
                    return Ok();

                _logger.LogInformation("Received message from {Phone}: {MessagePreview}",
                    fromNumber,
                    messageText.Length > 60 ? $"{messageText[..60]}..." : messageText);

                var session = _session.GetOrCreateSession(fromNumber);

                await _chatLog.LogInboundAsync(
                    fromNumber,
                    session.Name,
                    session.Email,
                    messageText,
                    session.State == UserState.Verified ? "chat" : "onboarding");

                var replyText = await ProcessMessageAsync(session, messageText);

                if (!string.IsNullOrWhiteSpace(replyText))
                {
                    await _whatsApp.SendMessageAsync(fromNumber, replyText);

                    await _chatLog.LogOutboundAsync(
                        fromNumber,
                        session.Name,
                        session.Email,
                        replyText,
                        session.State == UserState.Verified ? "chat" : "onboarding");
                }

                _session.UpdateSession(session);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return Ok();
            }
        }

        private bool VerifySignature(string rawBody)
        {
            var appSecret = _config["MetaWhatsApp:AppSecret"];
            var signature = Request.Headers["X-Hub-Signature-256"].ToString();

            if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(signature))
                return true;

            if (!signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
                return false;

            var expectedHash = signature["sha256=".Length..];

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
            var computed = Convert.ToHexString(hashBytes).ToLowerInvariant();

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed),
                Encoding.UTF8.GetBytes(expectedHash.ToLowerInvariant()));
        }

        private static bool TryExtractIncomingMessage(string json, out string from, out string text)
        {
            from = string.Empty;
            text = string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("entry", out var entry) || entry.GetArrayLength() == 0)
                    return false;

                var changes = entry[0].GetProperty("changes");
                if (changes.GetArrayLength() == 0)
                    return false;

                var value = changes[0].GetProperty("value");

                if (!value.TryGetProperty("messages", out var messages) || messages.GetArrayLength() == 0)
                    return false;

                var msg = messages[0];

                from = msg.GetProperty("from").GetString() ?? string.Empty;
                var type = msg.GetProperty("type").GetString() ?? string.Empty;

                if (type == "text" && msg.TryGetProperty("text", out var txtObj))
                {
                    text = txtObj.GetProperty("body").GetString() ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(from);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private Task<string> ProcessMessageAsync(UserSession session, string messageText)
        {
            return session.State switch
            {
                UserState.New => Task.FromResult(HandleNewUser(session)),
                UserState.AwaitingName => Task.FromResult(HandleNameInput(session, messageText)),
                UserState.AwaitingEmail => Task.FromResult(HandleEmailInput(session, messageText)),
                UserState.Verified => HandleVerifiedUserAsync(session, messageText),
                _ => Task.FromResult("Something went wrong. Please try again.")
            };
        }

        private static string HandleNewUser(UserSession session)
        {
            session.State = UserState.AwaitingName;
            return "👋 Hi! I’m Aje's Demo Assistant (powered by AI). Reply with your full name to start.";
        }

        private static string HandleNameInput(UserSession session, string input)
        {
            var raw = (input ?? string.Empty).Trim();

            if (GreetingSet.Contains(raw))
                return "😅 I need your *full name* please (e.g., Olufemi Keripe).";

            if (raw.Length < 2)
                return "Please enter a valid name (at least 2 characters).";

            if (raw.Length > 100)
                return "That name is too long. Please enter a shorter name.";

            if (!NameRegex().IsMatch(raw))
                return "Please enter a valid name using only letters.";

            session.Name = raw;
            session.State = UserState.AwaitingEmail;

            return $@"Nice to meet you, *{raw}*! 🎉

Now, please enter your *email address* to complete verification.";
        }

        private static string HandleEmailInput(UserSession session, string input)
        {
            var email = (input ?? string.Empty).Trim().ToLowerInvariant();

            if (!EmailRegex().IsMatch(email))
            {
                return @"That doesn't look like a valid email address. 🤔

Please enter a valid email (e.g., name@example.com)";
            }

            session.Email = email;
            session.State = UserState.Verified;

            return $@"✅ *You're all set, {session.Name}!*

Try:
• */products*
• */search cake*
• */product SKU-001*
• */flight*";
        }

        private async Task<string> HandleVerifiedUserAsync(UserSession session, string messageText)
        {
            var text = (messageText ?? string.Empty).Trim();
            var lower = text.ToLowerInvariant();

            // ===== Flight conversation in-progress =====
            if (session.FlightStep != FlightStep.None && lower != "/flightcancel")
            {
                return await ContinueFlightFlowAsync(session, text);
            }

            // --- core commands ---
            if (lower is "/help")
            {
                return $@"🆘 *Help Menu*

Hi {session.Name}! Commands:
• */reset* - Start over
• */help* - Show help
• */status* - Your profile
• */clear* - Clear memory
• */products* - Latest products
• */search <keyword>* - Search products
• */product <SKU>* - View product details
• */flight* - Search flight prices
• */flightcancel* - Cancel flight search";
            }

            if (lower is "/status")
            {
                return $@"📊 *Your Account Status*

👤 Name: {session.Name}
📧 Email: {session.Email}
📱 Phone: {session.PhoneNumber}
🕐 Session started: {session.CreatedAt:MMM dd, yyyy HH:mm} UTC
💬 Messages in memory: {session.ConversationHistory.Count}";
            }

            if (lower is "/clear")
            {
                session.ConversationHistory.Clear();
                return "🧹 Memory cleared. What are we talking about now?";
            }

            if (lower is "/reset" or "/restart")
            {
                _session.RemoveSession(session.PhoneNumber);
                return "🔄 Your session has been reset.\n\nSend any message to start over!";
            }

            // ===== Flight command =====
            if (lower is "/flight" or "/flights")
            {
                session.ResetFlightFlow();
                session.FlightStep = FlightStep.ChooseAirline;

                var airlines = _scrapingOpt.Airlines ?? Array.Empty<AirlineTarget>();
                if (airlines.Length == 0)
                    return "No airlines configured on the server yet. Add them under *Scraping:Airlines*.";

                var list = string.Join("\n", airlines.Select((a, i) => $"{i + 1}. *{a.Name}* ({a.SourceKey})"));
                return "✈️ *Flight Search*\n\nChoose an airline by replying with the number or source key:\n\n" + list;
            }

            // --- catalog commands ---
            if (lower is "/products")
            {
                var items = await _catalog.GetLatestProductsAsync(10);
                if (items.Count == 0) return "No products found yet. (Catalog is empty)";

                var lines = items.Select(p =>
                    $"• *{p.Name}* ({p.Sku}) — {p.Currency} {p.Price:N0} {(p.StockQty > 0 ? "" : "⚠️ Out of stock")}");

                return "🛍️ *Latest Products*\n\n" + string.Join("\n", lines) +
                       "\n\nUse: */product <SKU>* to view details.";
            }

            if (lower.StartsWith("/search ", StringComparison.Ordinal))
            {
                var q = text.Length > 8 ? text[8..].Trim() : string.Empty;
                if (q.Length < 2) return "Give me a keyword to search. Example: */search cake*";

                var results = await _catalog.SearchAsync(q, 10);
                if (results.Count == 0) return $"No matches for *{q}*.";

                var lines = results.Select(p => $"• *{p.Name}* ({p.Sku}) — {p.Currency} {p.Price:N0}");

                return $"🔎 *Search Results for:* {q}\n\n" + string.Join("\n", lines) +
                       "\n\nUse: */product <SKU>* to view details.";
            }

            if (lower.StartsWith("/product ", StringComparison.Ordinal))
            {
                var sku = text.Length > 9 ? text[9..].Trim() : string.Empty;
                if (sku.Length < 2) return "Use: */product SKU-001*";

                var p = await _catalog.GetBySkuAsync(sku);
                if (p == null) return $"No product found for SKU *{sku}*.";

                var primary = p.Images.FirstOrDefault(x => x.IsPrimary)?.Url
                              ?? p.Images.FirstOrDefault()?.Url;

                if (!string.IsNullOrWhiteSpace(primary))
                {
                    await _whatsApp.SendImageAsync(session.PhoneNumber, primary, p.Name);

                    await _chatLog.LogOutboundAsync(session.PhoneNumber, session.Name, session.Email,
                        $"[IMAGE] {primary}", "catalog");
                }

                var stock = p.StockQty > 0 ? $"✅ In stock ({p.StockQty})" : "⚠️ Out of stock";

                return $@"🧾 *Product Details*

*{p.Name}*
SKU: {p.Sku}
Price: {p.Currency} {p.Price:N0}
Stock: {stock}

{(string.IsNullOrWhiteSpace(p.Description) ? "" : $"About: {p.Description}")}";
            }

            // --- fallback to LLM ---
            try
            {
                return await _llm.GetResponseAsync(session, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM error for {Phone}", session.PhoneNumber);
                return "😕 I’m having trouble right now. Try again in a moment.";
            }
        }

        private async Task<string> ContinueFlightFlowAsync(UserSession session, string text)
        {
            var airlines = _scrapingOpt.Airlines ?? Array.Empty<AirlineTarget>();

            switch (session.FlightStep)
            {
                case FlightStep.ChooseAirline:
                    {
                        var pick = text.Trim();

                        AirlineTarget? chosen = null;

                        if (int.TryParse(pick, out var idx))
                        {
                            if (idx >= 1 && idx <= airlines.Length) chosen = airlines[idx - 1];
                        }
                        else
                        {
                            chosen = airlines.FirstOrDefault(a =>
                                string.Equals(a.SourceKey, pick, StringComparison.OrdinalIgnoreCase));
                        }

                        if (chosen == null)
                            return "Invalid selection. Reply with the number or source key of the airline.";

                        session.FlightDraft.SourceKey = chosen.SourceKey;
                        session.FlightStep = FlightStep.ChoosePricingMode;

                        return @"Choose pricing mode:
1) *Auto* (recommended)
2) *Amadeus* (API live pricing)
3) *DeepLink* (booking page)

Reply with 1, 2 or 3.";
                    }

                case FlightStep.ChoosePricingMode:
                    {
                        var m = text.Trim();
                        session.FlightPricingMode = m switch
                        {
                            "1" => FlightPricingMode.Auto,
                            "2" => FlightPricingMode.Amadeus,
                            "3" => FlightPricingMode.DeepLink,
                            _ => FlightPricingMode.Auto
                        };

                        session.FlightStep = FlightStep.TripType;
                        return "Trip type: reply *1* for One-way or *2* for Round-trip.";
                    }

                case FlightStep.TripType:
                    {
                        var t = text.Trim();
                        session.FlightDraft.IsRoundTrip = t == "2";
                        session.FlightStep = FlightStep.FromAirport;
                        return "Enter *Departure airport* IATA code (e.g., LOS).";
                    }

                case FlightStep.FromAirport:
                    {
                        var code = NormalizeIata(text);
                        if (code == null) return "Invalid. Enter a 3-letter IATA code like *LOS*.";

                        session.FlightDraft.From = code;
                        session.FlightStep = FlightStep.ToAirport;
                        return "Enter *Destination airport* IATA code (e.g., LHR).";
                    }

                case FlightStep.ToAirport:
                    {
                        var code = NormalizeIata(text);
                        if (code == null) return "Invalid. Enter a 3-letter IATA code like *LHR*.";

                        session.FlightDraft.To = code;
                        session.FlightStep = FlightStep.DepartDate;
                        return "Enter *Departure date* in format *YYYY-MM-DD* (example: 2026-02-10).";
                    }

                case FlightStep.DepartDate:
                    {
                        if (!TryParseDateOnly(text, out var d))
                            return "Invalid date. Use *YYYY-MM-DD* (example: 2026-02-10).";

                        session.FlightDraft.DepartDate = d;

                        if (session.FlightDraft.IsRoundTrip)
                        {
                            session.FlightStep = FlightStep.ReturnDate;
                            return "Enter *Return date* in format *YYYY-MM-DD* (example: 2026-02-20).";
                        }

                        session.FlightStep = FlightStep.Passengers;
                        return "Passengers: reply like *1 0 0* meaning Adults Children Infants (example: 1 2 0).";
                    }

                case FlightStep.ReturnDate:
                    {
                        if (!TryParseDateOnly(text, out var d))
                            return "Invalid date. Use *YYYY-MM-DD* (example: 2026-02-20).";

                        if (session.FlightDraft.DepartDate.HasValue && d <= session.FlightDraft.DepartDate.Value)
                            return "Return date must be after departure date.";

                        session.FlightDraft.ReturnDate = d;
                        session.FlightStep = FlightStep.Passengers;
                        return "Passengers: reply like *1 0 0* meaning Adults Children Infants (example: 1 2 0).";
                    }

                case FlightStep.Passengers:
                    {
                        if (!TryParsePassengers(text, out var a, out var c, out var i))
                            return "Invalid format. Reply like *1 0 0* (Adults Children Infants).";

                        if (a < 1) return "Adults must be at least 1.";
                        if (i > a) return "Infants cannot be more than adults.";

                        session.FlightDraft.Adults = a;
                        session.FlightDraft.Children = c;
                        session.FlightDraft.Infants = i;

                        session.FlightStep = FlightStep.Confirm;

                        var depart = session.FlightDraft.DepartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
                        var ret = session.FlightDraft.ReturnDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

                        return $@"✅ Confirm search:

Airline: *{session.FlightDraft.SourceKey}*
Mode: *{session.FlightPricingMode}*
Route: *{session.FlightDraft.From} → {session.FlightDraft.To}*
Trip: *{(session.FlightDraft.IsRoundTrip ? "Round-trip" : "One-way")}*
Depart: *{depart}*{(session.FlightDraft.IsRoundTrip ? $"\nReturn: *{ret}*" : "")}
Passengers: *{session.FlightDraft.Adults}A {session.FlightDraft.Children}C {session.FlightDraft.Infants}I*

Reply *YES* to fetch price, or */flightcancel*.";
                    }

                case FlightStep.Confirm:
                    {
                        if (!text.Equals("yes", StringComparison.OrdinalIgnoreCase))
                        {
                            session.ResetFlightFlow();
                            return "Flight search cancelled.";
                        }

                        var chosen = airlines.FirstOrDefault(a =>
                            string.Equals(a.SourceKey, session.FlightDraft.SourceKey, StringComparison.OrdinalIgnoreCase));

                        if (chosen == null)
                        {
                            session.ResetFlightFlow();
                            return "Airline not found in server config. Cancelled.";
                        }

                        // Quote
                        var quote = await _flightPricing.GetQuoteAsync(chosen, session.FlightDraft, session.FlightPricingMode, HttpContext.RequestAborted);

                        session.ResetFlightFlow();

                        var priceLine = quote.Price.HasValue
                            ? $"Price: *{quote.Currency} {quote.Price.Value:N0}*"
                            : "Price: *(not available via API)*";

                        var exact = quote.IsPriceExact ? "✅ Live price" : "ℹ️ Booking link / guidance";

                        return $@"✈️ *Flight Result* ({exact})

Airline: *{chosen.Name}*
Route: *{session.FlightDraft.From} → {session.FlightDraft.To}*
{priceLine}

{quote.Message}

Book here: {quote.BookingUrl}";
                    }

                default:
                    session.ResetFlightFlow();
                    return "Flight flow reset. Send */flight* to start again.";
            }
        }

        private static string? NormalizeIata(string input)
        {
            var s = (input ?? "").Trim().ToUpperInvariant();
            if (s.Length != 3) return null;
            if (!s.All(char.IsLetter)) return null;
            return s;
        }

        private static bool TryParseDateOnly(string input, out DateOnly date)
        {
            date = default;
            var s = (input ?? "").Trim();

            if (!DateTime.TryParseExact(
                    s,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dt))
                return false;

            date = DateOnly.FromDateTime(dt);
            return true;
        }

        private static bool TryParsePassengers(string input, out int adults, out int children, out int infants)
        {
            adults = 0; children = 0; infants = 0;

            var parts = (input ?? "")
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 3) return false;

            return int.TryParse(parts[0], out adults)
                && int.TryParse(parts[1], out children)
                && int.TryParse(parts[2], out infants);
        }

        [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
        private static partial Regex EmailRegex();

        [GeneratedRegex(@"^[a-zA-Z\s\-'\.]+$")]
        private static partial Regex NameRegex();
    }
}
