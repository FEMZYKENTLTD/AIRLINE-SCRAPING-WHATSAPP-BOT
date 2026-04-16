using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Models;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;
using WhatsAppBot.Services.Automation;
using WhatsAppBot.Services.Flights;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Reservations;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Controllers
{
    [ApiController]
    [Route("webhook")]
    [Route("api/webhook")]
    public partial class WhatsAppWebhookController : ControllerBase
    {
        // ── Services ─────────────────────────────────────────────────────
        private readonly ILLMService _llm;
        private readonly ISessionService _session;
        private readonly IWhatsAppService _whatsApp;
        private readonly IChatLogService _chatLog;
        private readonly IProductCatalogService _catalog;
        private readonly FlightPricingService _flightPricing;
        private readonly IReservationService _reservations;
        private readonly BookingOrchestrator _orchestrator;
        private readonly ScrapingOptions _scrapingOpt;
        private readonly ILogger<WhatsAppWebhookController> _logger;
        private readonly IConfiguration _config;

        // ── Constants ─────────────────────────────────────────────────────
        private static readonly HashSet<string> GreetingSet = new(
            new[] {
                "hi", "hello", "hey", "yo", "sup", "wassup",
                "good morning", "good afternoon", "good evening",
                "hii", "heyy", "hola", "hi fam", "hello fam", "hey fam",
                "howdy", "greetings", "salut", "bonjour"
            },
            StringComparer.OrdinalIgnoreCase);

        // ── Constructor ───────────────────────────────────────────────────
        public WhatsAppWebhookController(
            ILLMService llm,
            ISessionService session,
            IWhatsAppService whatsApp,
            IChatLogService chatLog,
            IProductCatalogService catalog,
            FlightPricingService flightPricing,
            IReservationService reservations,
            BookingOrchestrator orchestrator,
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
            _reservations = reservations;
            _orchestrator = orchestrator;
            _scrapingOpt = scrapingOptions.Value;
            _logger = logger;
            _config = config;
        }

        // ═══════════════════════════════════════════════════════════════════
        // WEBHOOK VERIFICATION (Meta requirement)
        // ═══════════════════════════════════════════════════════════════════
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
                _logger.LogInformation("✅ Meta webhook verified successfully.");
                return Content(challenge ?? string.Empty, "text/plain");
            }

            _logger.LogWarning("❌ Meta webhook verification failed. Token mismatch.");
            return Unauthorized();
        }

        // ═══════════════════════════════════════════════════════════════════
        // RECEIVE MESSAGE
        // ═══════════════════════════════════════════════════════════════════
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
                    _logger.LogWarning("❌ Invalid webhook signature.");
                    return Unauthorized();
                }

                if (!TryExtractIncomingMessage(rawBody, out var fromNumber, out var messageText))
                    return Ok();

                messageText = (messageText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(messageText))
                    return Ok();

                _logger.LogInformation("📨 Message from {Phone}: {Preview}",
                    fromNumber,
                    messageText.Length > 60 ? $"{messageText[..60]}..." : messageText);

                var session = _session.GetOrCreateSession(fromNumber);
                var phase = session.State == UserState.Verified ? "chat" : "onboarding";

                await _chatLog.LogInboundAsync(fromNumber, session.Name, session.Email, messageText, phase);

                var replyText = await ProcessMessageAsync(session, messageText);

                if (!string.IsNullOrWhiteSpace(replyText))
                {
                    await _whatsApp.SendMessageAsync(fromNumber, replyText);
                    await _chatLog.LogOutboundAsync(fromNumber, session.Name, session.Email, replyText, phase);
                }

                _session.UpdateSession(session);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing webhook");
                return Ok(); // Always return 200 to Meta
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECURITY: Verify webhook signature from Meta
        // ═══════════════════════════════════════════════════════════════════
        private bool VerifySignature(string rawBody)
        {
            var appSecret = _config["MetaWhatsApp:AppSecret"];
            var signature = Request.Headers["X-Hub-Signature-256"].ToString();

            if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(signature))
                return true; // Allow if not configured (dev mode)

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

        // ═══════════════════════════════════════════════════════════════════
        // PARSE: Extract message from Meta webhook payload
        // ═══════════════════════════════════════════════════════════════════
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
                if (changes.GetArrayLength() == 0) return false;

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
            catch { return false; }
        }

        // ═══════════════════════════════════════════════════════════════════
        // MAIN ROUTER: Direct to correct state handler
        // ═══════════════════════════════════════════════════════════════════
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

        // ═══════════════════════════════════════════════════════════════════
        // ONBOARDING FLOW
        // ═══════════════════════════════════════════════════════════════════
        private string HandleNewUser(UserSession session)
        {
            var botName = _config["Bot:Name"] ?? "Femzyk_Aje_Bot";
            var company = _config["Bot:Company"] ?? "FEMZYK ENTERPRISES LTD";
            session.State = UserState.AwaitingName;
            return $@"👋 Welcome to *{botName}*!
Your AI-powered travel & shopping assistant by *{company}*.

Please reply with your *full name* to get started.";
        }

        private string HandleNameInput(UserSession session, string input)
        {
            var raw = (input ?? string.Empty).Trim();

            if (GreetingSet.Contains(raw))
                return "😅 I need your *full name* please (e.g., Olufemi Keripe).";

            if (raw.Length < 2)
                return "Please enter a valid name (at least 2 characters).";

            if (raw.Length > 100)
                return "That name is too long. Please enter a shorter name.";

            if (!NameRegex().IsMatch(raw))
                return "Please enter a valid name using only letters, spaces, or hyphens.";

            session.Name = raw;
            session.State = UserState.AwaitingEmail;

            return $@"Nice to meet you, *{raw}*! 🎉

Now, please enter your *email address* to complete verification.
(Used for booking confirmations)";
        }

        private string HandleEmailInput(UserSession session, string input)
        {
            var email = (input ?? string.Empty).Trim().ToLowerInvariant();

            if (!EmailRegex().IsMatch(email))
                return @"That doesn't look like a valid email. 🤔
Please enter a valid email (e.g., name@example.com)";

            session.Email = email;
            session.State = UserState.Verified;

            var botName = _config["Bot:Name"] ?? "Femzyk_Aje_Bot";

            return $@"✅ *You're all set, {session.Name}!*

Welcome to *{botName}*! Here's what I can do:

✈️ */flight* → Search & book flights
📋 */mybookings* → View your reservations
❌ */cancelbooking* → Cancel a reservation
🛍️ */products* → Browse products
💬 Just *type anything* to chat with AI!
🆘 */help* → Full command list";
        }

        // ═══════════════════════════════════════════════════════════════════
        // VERIFIED USER: Route to correct feature
        // ═══════════════════════════════════════════════════════════════════
        private async Task<string> HandleVerifiedUserAsync(UserSession session, string messageText)
        {
            var text = (messageText ?? string.Empty).Trim();
            var lower = text.ToLowerInvariant();

            // ── Active flow routing ──────────────────────────────────────
            if (session.FlightStep != FlightStep.None && lower != "/flightcancel")
            {
                // Search flow (steps 1-9)
                if (session.FlightStep >= FlightStep.ChooseAirline &&
                    session.FlightStep <= FlightStep.Confirm)
                    return await ContinueFlightSearchFlowAsync(session, text);

                // Booking flow (steps 10-21)
                if (session.FlightStep >= FlightStep.ViewResults &&
                    session.FlightStep <= FlightStep.BookingComplete)
                    return await ContinueBookingFlowAsync(session, text);

                // Cancellation flow (steps 30-31)
                if (session.FlightStep >= FlightStep.CancelConfirm)
                    return await ContinueCancelFlowAsync(session, text);
            }

            // ── Config shortcuts ─────────────────────────────────────────
            var botName = _config["Bot:Name"] ?? "Femzyk_Aje_Bot";
            var company = _config["Bot:Company"] ?? "FEMZYK ENTERPRISES LTD";
            var supportEmail = _config["Bot:SupportEmail"] ?? "femzykenterprisesltd@gmail.com";

            // ── Help / Menu ──────────────────────────────────────────────
            if (lower is "/help" or "help" or "menu" or "commands" or "/menu" or "/commands")
            {
                return $@"🤖 *{botName} — Command Center*

━━━━━━━━━━━━━━━━━━━━━━━
✈️ *FLIGHT COMMANDS*
━━━━━━━━━━━━━━━━━━━━━━━
• */flight* — Search & book flights
• */flightcancel* — Cancel flight search
• */mybookings* — View your reservations
• */cancelbooking* — Cancel a reservation

━━━━━━━━━━━━━━━━━━━━━━━
🛍️ *CATALOG COMMANDS*
━━━━━━━━━━━━━━━━━━━━━━━
• */products* — Browse latest products
• */search <keyword>* — Search products
• */product <SKU>* — View product details

━━━━━━━━━━━━━━━━━━━━━━━
✈️ *AIRLINES*
━━━━━━━━━━━━━━━━━━━━━━━
• 🇳🇬 Arik Air (arikair)
• 🇳🇬 Air Peace (airpeace)
• 🇹🇷 Turkish Airlines (turkish)
• 🇩🇪 Lufthansa (lufthansa)

━━━━━━━━━━━━━━━━━━━━━━━
💬 *AI ASSISTANT*
━━━━━━━━━━━━━━━━━━━━━━━
• Type anything to chat with AI
• Ask about flights, destinations, travel tips
• Get real-time data from live sources

━━━━━━━━━━━━━━━━━━━━━━━
⚙️ *ACCOUNT*
━━━━━━━━━━━━━━━━━━━━━━━
• */status* — Your profile
• */clear* — Clear chat memory
• */reset* — Start fresh session
• */help* — This menu

━━━━━━━━━━━━━━━━━━━━━━━
📞 *SUPPORT*
━━━━━━━━━━━━━━━━━━━━━━━
📧 {supportEmail}
🏢 {company}

_Powered by Azure OpenAI + Live Data_";
            }

            // ── Status ───────────────────────────────────────────────────
            if (lower is "/status")
            {
                return $@"📊 *Your Account*

👤 Name: {session.Name}
📧 Email: {session.Email}
📱 Phone: {session.PhoneNumber}
🕐 Session: {session.CreatedAt:MMM dd, yyyy HH:mm} UTC
💬 Memory: {session.ConversationHistory.Count} messages
✈️ Active flow: {(session.FlightStep == FlightStep.None ? "None" : session.FlightStep.ToString())}";
            }

            // ── Clear memory ─────────────────────────────────────────────
            if (lower is "/clear")
            {
                session.ConversationHistory.Clear();
                return "🧹 Memory cleared! What would you like to talk about?";
            }

            // ── Reset session ────────────────────────────────────────────
            if (lower is "/reset" or "/restart")
            {
                _session.RemoveSession(session.PhoneNumber);
                return "🔄 Your session has been reset.\n\nSend any message to start over!";
            }

            // ── Cancel active flight search ──────────────────────────────
            if (lower is "/flightcancel")
            {
                session.ResetAll();
                return "✈️ Flight search cancelled.\n\nType */flight* to start a new search.";
            }

            // ── Start flight search ──────────────────────────────────────
            if (lower is "/flight" or "/flights")
            {
                session.ResetAll();
                session.FlightStep = FlightStep.ChooseAirline;

                var airlines = _scrapingOpt.Airlines ?? Array.Empty<AirlineTarget>();
                if (airlines.Length == 0)
                    return "⚠️ No airlines configured. Please contact support.";

                var list = string.Join("\n", airlines.Select((a, i) =>
                    $"{i + 1}. *{a.Name}* ({a.SourceKey})"));

                return $@"✈️ *Flight Search*

Choose an airline by replying with a number or source key:

{list}

Or type */flightcancel* to abort.";
            }

            // ── My bookings ──────────────────────────────────────────────
            if (lower is "/mybookings" or "/bookings" or "/reservations" or "/myreservations")
            {
                var bookings = await _reservations.GetUserReservationsAsync(
                    session.PhoneNumber, 5, HttpContext.RequestAborted);

                if (bookings.Count == 0)
                    return "📋 You have no reservations yet.\n\nType */flight* to search and book!";

                var lines = bookings.Select(r =>
                    $"• *{r.ReservationCode}* — {r.FromAirport}→{r.ToAirport} | *{r.Status}* | {r.DepartDate:yyyy-MM-dd}");

                return "📋 *Your Reservations* (last 5)\n\n" +
                       string.Join("\n", lines) +
                       "\n\nTo cancel: */cancelbooking*";
            }

            // ── Start cancellation flow ──────────────────────────────────
            if (lower is "/cancelbooking" or "/cancel" or "/cancelreservation")
            {
                session.FlightStep = FlightStep.CancelConfirm;
                return "🚫 *Cancel Booking*\n\nEnter your *Reservation Code* (e.g., FZK-260410-1234):";
            }

            // ── Products ─────────────────────────────────────────────────
            if (lower is "/products")
            {
                var items = await _catalog.GetLatestProductsAsync(10);
                if (items.Count == 0) return "No products found yet. (Catalog is empty)";

                var lines = items.Select(p =>
                    $"• *{p.Name}* ({p.Sku}) — {p.Currency} {p.Price:N0} {(p.StockQty > 0 ? "✅" : "⚠️ Out of stock")}");

                return "🛍️ *Latest Products*\n\n" + string.Join("\n", lines) +
                       "\n\nUse */product <SKU>* to view details.";
            }

            // ── Search products ───────────────────────────────────────────
            if (lower.StartsWith("/search ", StringComparison.Ordinal))
            {
                var q = text.Length > 8 ? text[8..].Trim() : string.Empty;
                if (q.Length < 2) return "Give me a keyword. Example: */search cake*";

                var results = await _catalog.SearchAsync(q, 10);
                if (results.Count == 0) return $"No matches for *{q}*. Try a different keyword.";

                var lines = results.Select(p => $"• *{p.Name}* ({p.Sku}) — {p.Currency} {p.Price:N0}");
                return $"🔎 *Results for:* {q}\n\n" + string.Join("\n", lines) +
                       "\n\nUse */product <SKU>* for details.";
            }

            // ── Product detail ────────────────────────────────────────────
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

            // ── Fallback: AI response ─────────────────────────────────────
            try
            {
                return await _llm.GetResponseAsync(session, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM error for {Phone}", session.PhoneNumber);
                return "😕 I'm having trouble right now. Try again in a moment.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // FLIGHT SEARCH FLOW (Steps 1-9)
        // ═══════════════════════════════════════════════════════════════════
        private async Task<string> ContinueFlightSearchFlowAsync(UserSession session, string text)
        {
            var airlines = _scrapingOpt.Airlines ?? Array.Empty<AirlineTarget>();

            switch (session.FlightStep)
            {
                case FlightStep.ChooseAirline:
                    AirlineTarget? chosen = null;
                    if (int.TryParse(text.Trim(), out var idx) && idx >= 1 && idx <= airlines.Length)
                        chosen = airlines[idx - 1];
                    else
                        chosen = airlines.FirstOrDefault(a =>
                            a.SourceKey.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (chosen == null)
                        return "Invalid selection. Reply with a number or source key.";

                    session.FlightDraft.SourceKey = chosen.SourceKey;
                    session.FlightStep = FlightStep.ChoosePricingMode;

                    return @"Choose pricing mode:
1) *Auto* (recommended — tries all sources)
2) *Amadeus* (live API pricing)
3) *DeepLink* (booking page link)

Reply with 1, 2 or 3.";

                case FlightStep.ChoosePricingMode:
                    session.FlightPricingMode = text.Trim() switch
                    {
                        "2" => FlightPricingMode.Amadeus,
                        "3" => FlightPricingMode.DeepLink,
                        _ => FlightPricingMode.Auto
                    };
                    session.FlightStep = FlightStep.TripType;
                    return "Trip type:\n*1* — One-way\n*2* — Round-trip\n\nReply 1 or 2.";

                case FlightStep.TripType:
                    session.FlightDraft.IsRoundTrip = text.Trim() == "2";
                    session.FlightStep = FlightStep.FromAirport;
                    return "Enter *Departure airport* IATA code (e.g., *LOS* for Lagos):";

                case FlightStep.FromAirport:
                    var fromCode = NormalizeIata(text);
                    if (fromCode == null) return "Invalid. Enter a 3-letter IATA code like *LOS*.";
                    session.FlightDraft.From = fromCode;
                    session.FlightStep = FlightStep.ToAirport;
                    return "Enter *Destination airport* IATA code (e.g., *LHR* for London):";

                case FlightStep.ToAirport:
                    var toCode = NormalizeIata(text);
                    if (toCode == null) return "Invalid. Enter a 3-letter IATA code like *LHR*.";
                    session.FlightDraft.To = toCode;
                    session.FlightStep = FlightStep.DepartDate;
                    return "Enter *Departure date* (format: YYYY-MM-DD)\nExample: 2026-02-10";

                case FlightStep.DepartDate:
                    if (!TryParseDateOnly(text, out var departDate))
                        return "Invalid date. Use YYYY-MM-DD (e.g., 2026-02-10).";
                    session.FlightDraft.DepartDate = departDate;

                    if (session.FlightDraft.IsRoundTrip)
                    {
                        session.FlightStep = FlightStep.ReturnDate;
                        return "Enter *Return date* (format: YYYY-MM-DD)\nExample: 2026-02-20";
                    }
                    session.FlightStep = FlightStep.Passengers;
                    return "Passengers: reply like *1 0 0*\n(Adults Children Infants)\nExample: 2 1 0";

                case FlightStep.ReturnDate:
                    if (!TryParseDateOnly(text, out var returnDate))
                        return "Invalid date. Use YYYY-MM-DD.";
                    if (session.FlightDraft.DepartDate.HasValue && returnDate <= session.FlightDraft.DepartDate.Value)
                        return "Return date must be *after* departure date.";
                    session.FlightDraft.ReturnDate = returnDate;
                    session.FlightStep = FlightStep.Passengers;
                    return "Passengers: reply like *1 0 0*\n(Adults Children Infants)";

                case FlightStep.Passengers:
                    if (!TryParsePassengers(text, out var adults, out var children, out var infants))
                        return "Invalid format. Reply exactly like: *1 0 0*";
                    if (adults < 1) return "Must have at least 1 adult.";
                    if (infants > adults) return "Infants cannot exceed number of adults.";

                    session.FlightDraft.Adults = adults;
                    session.FlightDraft.Children = children;
                    session.FlightDraft.Infants = infants;
                    session.FlightStep = FlightStep.Confirm;

                    var d = session.FlightDraft;
                    var retStr = d.IsRoundTrip && d.ReturnDate.HasValue
                        ? $"\nReturn: *{d.ReturnDate:yyyy-MM-dd}*" : "";

                    return $@"✅ *Confirm Search:*

✈️ Airline: *{d.SourceKey}*
🔄 Mode: *{session.FlightPricingMode}*
📍 Route: *{d.From} → {d.To}*
📅 Depart: *{d.DepartDate:yyyy-MM-dd}*{retStr}
👥 Passengers: *{d.Adults}A {d.Children}C {d.Infants}I*

Reply *YES* to search prices, or */flightcancel* to abort.";

                case FlightStep.Confirm:
                    if (!text.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    {
                        session.ResetAll();
                        return "Search cancelled. Type */flight* to start again.";
                    }

                    var target = airlines.FirstOrDefault(a =>
                        a.SourceKey.Equals(session.FlightDraft.SourceKey, StringComparison.OrdinalIgnoreCase));

                    if (target == null) { session.ResetAll(); return "Airline not found."; }

                    await _whatsApp.SendMessageAsync(session.PhoneNumber,
                        "🔍 Searching for the best prices... please wait.");

                    var quote = await _flightPricing.GetQuoteAsync(
                        target, session.FlightDraft, session.FlightPricingMode, HttpContext.RequestAborted);

                    session.CurrentQuote = quote;
                    session.FlightStep = FlightStep.ViewResults;

                    var priceStr = quote.Price.HasValue
                        ? $"*{quote.Currency} {quote.Price.Value:N0}*"
                        : "*(varies — check booking page)*";

                    var priceType = quote.IsPriceExact ? "✅ Live price" : "ℹ️ Estimated / guidance";

                    return $@"✈️ *Flight Result* ({priceType})

🏢 Airline: *{target.Name}*
📍 Route: *{session.FlightDraft.From} → {session.FlightDraft.To}*
💰 Price: {priceStr}

{quote.Message}

🔗 {quote.BookingUrl}

━━━━━━━━━━━━━━━━━━━━━━━
Reply *BOOK* to reserve this flight
Reply *CANCEL* to abort";

                default:
                    session.ResetAll();
                    return "Flow error. Type */flight* to restart.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // BOOKING FLOW — Passenger collection (Steps 10-21)
        // ═══════════════════════════════════════════════════════════════════
        private async Task<string> ContinueBookingFlowAsync(UserSession session, string text)
        {
            if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                session.ResetAll();
                return "Booking cancelled. Type */flight* to start again.";
            }

            switch (session.FlightStep)
            {
                case FlightStep.ViewResults:
                    if (!text.Equals("book", StringComparison.OrdinalIgnoreCase))
                        return "Reply *BOOK* to reserve this flight, or *CANCEL* to abort.";

                    session.FlightStep = FlightStep.CollectFullName;
                    return "📝 *Booking Started!*\n\nEnter the primary passenger's *Full Name*\n(Exactly as it appears on passport or ID):";

                case FlightStep.CollectFullName:
                    if (text.Length < 3) return "Please enter a valid full name (at least 3 characters).";
                    session.PassengerDraft.FullName = text;

                    var nameParts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (nameParts.Length >= 2)
                    {
                        session.PassengerDraft.FirstName = nameParts[0];
                        session.PassengerDraft.LastName = nameParts[^1];
                    }

                    session.FlightStep = FlightStep.CollectDateOfBirth;
                    return "Enter *Date of Birth* (format: YYYY-MM-DD)\nExample: 1990-05-15";

                case FlightStep.CollectDateOfBirth:
                    if (!TryParseDateOnly(text, out var dob))
                        return "Invalid date. Use YYYY-MM-DD (e.g., 1990-05-15).";
                    session.PassengerDraft.DateOfBirth = dob;
                    session.FlightStep = FlightStep.CollectGender;
                    return "Enter *Gender*:\n*M* — Male\n*F* — Female";

                case FlightStep.CollectGender:
                    session.PassengerDraft.Gender = text.Trim().ToUpper() switch
                    {
                        "M" => "Male",
                        "F" => "Female",
                        _ => text.Trim()
                    };
                    session.FlightStep = FlightStep.CollectNationality;
                    return "Enter *Nationality* (e.g., Nigerian, British):";

                case FlightStep.CollectNationality:
                    session.PassengerDraft.Nationality = text.Trim();
                    session.FlightStep = FlightStep.CollectPassport;
                    return "Enter *Passport or National ID Number*:";

                case FlightStep.CollectPassport:
                    session.PassengerDraft.PassportNumber = text.Trim().ToUpper();
                    session.FlightStep = FlightStep.CollectPassportExpiry;
                    return "Enter *Passport Expiry Date* (format: YYYY-MM-DD):";

                case FlightStep.CollectPassportExpiry:
                    if (!TryParseDateOnly(text, out var expiry))
                        return "Invalid date. Use YYYY-MM-DD.";
                    if (expiry < DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)))
                        return "⚠️ Passport must be valid for at least 6 months from travel date.";
                    session.PassengerDraft.PassportExpiry = expiry;
                    session.FlightStep = FlightStep.CollectEmail;
                    return "Enter *Email Address* (for booking confirmation & tickets):";

                case FlightStep.CollectEmail:
                    if (!EmailRegex().IsMatch(text.Trim()))
                        return "Please enter a valid email address.";
                    session.PassengerDraft.Email = text.Trim().ToLowerInvariant();
                    session.FlightStep = FlightStep.CollectSeatPreference;
                    return "Seat Preference:\n*W* — Window\n*A* — Aisle\n*N* — No preference";

                case FlightStep.CollectSeatPreference:
                    session.PassengerDraft.SeatPreference = text.Trim().ToUpper() switch
                    {
                        "W" => "Window",
                        "A" => "Aisle",
                        _ => "No preference"
                    };
                    session.FlightStep = FlightStep.ConfirmBooking;

                    var fd = session.FlightDraft;
                    var pd = session.PassengerDraft;
                    var priceDisplay = session.CurrentQuote?.Price.HasValue == true
                        ? $"{session.CurrentQuote.Currency} {session.CurrentQuote.Price.Value:N0}"
                        : "TBD";

                    return $@"✅ *Verify All Details:*

━━━━━━━━━━━━━━━━━━━━━━━
✈️ *FLIGHT*
━━━━━━━━━━━━━━━━━━━━━━━
Airline: {fd.SourceKey}
Route: {fd.From} → {fd.To}
Depart: {fd.DepartDate:yyyy-MM-dd}
Passengers: {fd.Adults}A {fd.Children}C {fd.Infants}I
Price: ~{priceDisplay}

━━━━━━━━━━━━━━━━━━━━━━━
👤 *PASSENGER*
━━━━━━━━━━━━━━━━━━━━━━━
Name: *{pd.FullName}*
DOB: {pd.DateOfBirth:yyyy-MM-dd}
Gender: {pd.Gender}
Nationality: {pd.Nationality}
Passport: {pd.PassportNumber} (Exp: {pd.PassportExpiry:yyyy-MM-dd})
Email: {pd.Email}
Seat: {pd.SeatPreference}
━━━━━━━━━━━━━━━━━━━━━━━

Reply *CONFIRM* to create reservation
Reply *CANCEL* to abort";

                case FlightStep.ConfirmBooking:
                    if (!text.Equals("confirm", StringComparison.OrdinalIgnoreCase))
                    {
                        session.ResetAll();
                        return "Booking cancelled. Type */flight* to start again.";
                    }

                    var airline = _scrapingOpt.Airlines
                        .FirstOrDefault(a => a.SourceKey == session.FlightDraft.SourceKey);

                    if (airline == null) { session.ResetAll(); return "Airline config error. Please try again."; }

                    // Create reservation in database
                    var reservation = await _reservations.CreateReservationAsync(
                        session.PhoneNumber,
                        session.Name,
                        session.PassengerDraft.Email,
                        session.FlightDraft,
                        session.CurrentQuote!,
                        airline.Name,
                        HttpContext.RequestAborted);

                    // Attach passenger details
                    await _reservations.AttachPassengerAsync(
                        reservation.ReservationCode,
                        session.PassengerDraft,
                        HttpContext.RequestAborted);

                    // Fire automation in background (non-blocking)
                    _ = Task.Run(() => _orchestrator.ExecuteBookingAsync(
                        reservation.ReservationCode,
                        session.FlightDraft,
                        session.PassengerDraft,
                        default));

                    var resCode = reservation.ReservationCode;
                    session.ResetAll();

                    return $@"🎉 *Reservation Created!*

━━━━━━━━━━━━━━━━━━━━━━━
📋 Code: *{resCode}*
✈️ {airline.Name}: {reservation.FromAirport} → {reservation.ToAirport}
📅 {reservation.DepartDate:yyyy-MM-dd}
📊 Status: *Processing*
━━━━━━━━━━━━━━━━━━━━━━━

Our automation system is now securing your seats on the airline website. You will receive a confirmation once processed.

📋 View bookings: */mybookings*
❌ To cancel: */cancelbooking*
🆘 Help: */help*";

                default:
                    session.ResetAll();
                    return "Booking flow error. Type */flight* to restart.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // CANCELLATION FLOW (Steps 30-31)
        // ═══════════════════════════════════════════════════════════════════
        private async Task<string> ContinueCancelFlowAsync(UserSession session, string text)
        {
            switch (session.FlightStep)
            {
                case FlightStep.CancelConfirm:
                    var code = text.Trim().ToUpper();
                    var res = await _reservations.GetByCodeAsync(code, HttpContext.RequestAborted);

                    if (res == null || res.PhoneNumber != session.PhoneNumber)
                    {
                        session.ResetAll();
                        return "❌ Reservation not found or doesn't belong to this number.\n\nCheck your code with */mybookings*";
                    }

                    if (!res.CanCancel)
                    {
                        session.ResetAll();
                        return $"⚠️ Cannot cancel. Current status: *{res.Status}*\n\nContact support for help.";
                    }

                    session.PendingCancellationCode = code;
                    session.FlightStep = FlightStep.CancelProcessing;

                    return $@"⚠️ *Cancel Reservation {code}?*

{res.GetSummary()}

━━━━━━━━━━━━━━━━━━━━━━━
Reply *YES* to confirm cancellation
Reply *NO* to keep your booking";

                case FlightStep.CancelProcessing:
                    if (!text.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    {
                        session.ResetAll();
                        return "✅ Cancellation aborted. Your booking is safe!";
                    }

                    var success = await _orchestrator.ExecuteCancellationAsync(
                        session.PendingCancellationCode!,
                        "User requested cancellation via WhatsApp",
                        HttpContext.RequestAborted);

                    session.ResetAll();

                    return success
                        ? @"✅ *Reservation Successfully Cancelled!*

Your booking has been cancelled. Any applicable refund will be processed within 5-10 business days.

Type */flight* to book a new flight."
                        : @"❌ *Cancellation Failed*

We couldn't automatically cancel this reservation. Please contact support:
📧 femzykenterprisesltd@gmail.com";

                default:
                    session.ResetAll();
                    return "Type */cancelbooking* to try again.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // HELPER METHODS
        // ═══════════════════════════════════════════════════════════════════
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
            if (!DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
                return false;
            date = DateOnly.FromDateTime(dt);
            return true;
        }

        private static bool TryParsePassengers(string input, out int adults, out int children, out int infants)
        {
            adults = 0; children = 0; infants = 0;
            var parts = (input ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;
            return int.TryParse(parts[0], out adults) &&
                   int.TryParse(parts[1], out children) &&
                   int.TryParse(parts[2], out infants);
        }

        [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
        private static partial Regex EmailRegex();

        [GeneratedRegex(@"^[a-zA-Z\s\-'\.]+$")]
        private static partial Regex NameRegex();
    }
}