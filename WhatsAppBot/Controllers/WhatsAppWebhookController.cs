using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
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
using WhatsAppBot.Services.Media;
using WhatsAppBot.Services.Reservations;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Controllers
{
    /// <summary>
    /// WhatsApp Cloud API webhook.
    ///
    /// ARCHITECTURE (Phase 2 — multi-channel consistency):
    ///   Webhook (this controller = adapter layer only)
    ///     → shared application services (User / PersistentSession / Conversation /
    ///       ServiceRequest / Audit / IntentRouter / LLM / FlightPricing / Reservations)
    ///     → persistence (SQLite via EF Core)
    ///     → external integrations (Meta Cloud API, Amadeus, scrapers)
    ///
    /// All business rules live in the shared services; the Telegram channel
    /// uses the exact same services. This controller contains no duplicate
    /// business logic — only channel-specific parsing and presentation.
    ///
    /// Guarantees:
    ///   • HMAC signature verification (when MetaWhatsApp:AppSecret is set)
    ///   • Idempotency: duplicate provider message IDs are ignored
    ///   • Every inbound/outbound message persisted with provider IDs
    ///   • Flight search/booking/cancellation/AI work tracked as ServiceRequests
    ///   • Audit events for webhook receipt, rejection, duplicates, requests
    /// </summary>
    [ApiController]
    [Route("webhook")]
    [Route("api/webhook")]
    public partial class WhatsAppWebhookController : ControllerBase
    {
        private const string Channel = "whatsapp";

        // ── Shared application services ──────────────────────────────────────
        private readonly ILLMService _llm;
        private readonly IWhatsAppService _whatsApp;
        private readonly IUserService _userService;
        private readonly IPersistentSessionService _sessionService;
        private readonly IConversationService _conversationService;
        private readonly IServiceRequestService _serviceRequests;
        private readonly IAuditService _auditService;
        private readonly IIntentRouter _intentRouter;
        private readonly FlightConversationService _flightConversation;
        private readonly IChatLogService _chatLog; // legacy phone-keyed log (preserved)
        private readonly IProductCatalogService _catalog;
        private readonly FlightPricingService _flightPricing;
        private readonly IReservationService _reservations;
        private readonly BookingOrchestrator _orchestrator;
        private readonly ScrapingOptions _scrapingOpt;
        private readonly ILogger<WhatsAppWebhookController> _logger;
        private readonly IConfiguration _config;
        private readonly IImageGenerationService _imageService;

        // ── Constants ─────────────────────────────────────────────────────────
        private static readonly HashSet<string> GreetingSet = new(
            new[] {
                "hi", "hello", "hey", "yo", "sup", "wassup",
                "good morning", "good afternoon", "good evening",
                "hii", "heyy", "hola", "hi fam", "hello fam", "hey fam",
                "howdy", "greetings", "salut", "bonjour"
            },
            StringComparer.OrdinalIgnoreCase);

        // ── Constructor ───────────────────────────────────────────────────────
        public WhatsAppWebhookController(
            ILLMService llm,
            IWhatsAppService whatsApp,
            IUserService userService,
            IPersistentSessionService sessionService,
            IConversationService conversationService,
            IServiceRequestService serviceRequests,
            IAuditService auditService,
            IIntentRouter intentRouter,
            FlightConversationService flightConversation,
            IChatLogService chatLog,
            IProductCatalogService catalog,
            FlightPricingService flightPricing,
            IReservationService reservations,
            BookingOrchestrator orchestrator,
            IOptions<ScrapingOptions> scrapingOptions,
            ILogger<WhatsAppWebhookController> logger,
            IConfiguration config,
            IImageGenerationService imageService)
        {
            _llm = llm;
            _whatsApp = whatsApp;
            _userService = userService;
            _sessionService = sessionService;
            _conversationService = conversationService;
            _serviceRequests = serviceRequests;
            _auditService = auditService;
            _intentRouter = intentRouter;
            _flightConversation = flightConversation;
            _chatLog = chatLog;
            _catalog = catalog;
            _flightPricing = flightPricing;
            _reservations = reservations;
            _orchestrator = orchestrator;
            _scrapingOpt = scrapingOptions.Value;
            _logger = logger;
            _config = config;
            _imageService = imageService;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // WEBHOOK VERIFICATION (Meta requirement)
        // ═══════════════════════════════════════════════════════════════════════
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

            _logger.LogWarning("Meta webhook verification failed. Token mismatch.");
            return Unauthorized();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // RECEIVE MESSAGE
        // ═══════════════════════════════════════════════════════════════════════
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
                    _logger.LogWarning("Invalid webhook signature — rejecting payload.");
                    await _auditService.LogAsync(
                        "WEBHOOK_REJECTED", "Webhook", null,
                        actorId: "system", channel: Channel,
                        details: "Invalid X-Hub-Signature-256",
                        ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                        ct: HttpContext.RequestAborted);
                    return Unauthorized();
                }

                if (!TryExtractIncomingMessage(rawBody, out var fromNumber, out var messageText, out var providerMessageId))
                    return Ok();

                messageText = (messageText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(messageText))
                    return Ok();

                _logger.LogInformation("Message from {Phone}: {Preview}",
                    fromNumber,
                    messageText.Length > 60 ? $"{messageText[..60]}..." : messageText);

                // ── Idempotency: same provider message twice → one business action ─
                if (!string.IsNullOrWhiteSpace(providerMessageId) &&
                    await _conversationService.ExistsByProviderMessageIdAsync(providerMessageId, HttpContext.RequestAborted))
                {
                    _logger.LogWarning("Duplicate WhatsApp delivery ignored: {MsgId}", providerMessageId);
                    await _auditService.LogAsync(
                        "WEBHOOK_DUPLICATE", "Message", providerMessageId,
                        actorId: "system", channel: Channel,
                        details: $"provider user {fromNumber}",
                        ct: HttpContext.RequestAborted);
                    return Ok();
                }

                var (user, _identity, created) = await _userService.FindOrCreateByChannelAsync(
                    Channel, fromNumber, null, HttpContext.RequestAborted);

                if (created)
                {
                    await _auditService.LogAsync(
                        "USER_CREATED", "User", user.Id.ToString(),
                        actorId: "system", channel: Channel,
                        details: $"provider user {fromNumber}",
                        ct: HttpContext.RequestAborted);
                }

                var session = await _sessionService.GetOrCreateSessionAsync(
                    Channel, fromNumber, user.Id, HttpContext.RequestAborted);

                // Persist the inbound message (shared platform message store)
                var inbound = await _conversationService.LogInboundAsync(
                    Channel, fromNumber, messageText, providerMessageId,
                    session.Id, user.Id, ct: HttpContext.RequestAborted);

                await _auditService.LogAsync(
                    "WEBHOOK_RECEIVED", "Message", inbound.Id.ToString(),
                    actorId: fromNumber, channel: Channel,
                    details: "inbound message",
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    ct: HttpContext.RequestAborted);

                // ── Restore persisted flow state ──────────────────────────────
                var flow = FlowContext.FromJson(session.ContextData);
                var working = flow.ToUserSession(fromNumber);
                working.ConversationHistory = await LoadPromptHistoryAsync(session.Id, providerMessageId, flow.HistoryClearedAtUtc);

                var phase = working.State == UserState.Verified ? "chat" : "onboarding";

                // Legacy phone-keyed chat log (preserved for continuity)
                await _chatLog.LogInboundAsync(fromNumber, working.Name, working.Email, messageText, phase);

                var replyText = await ProcessMessageAsync(working, messageText, user, session);

                if (!string.IsNullOrWhiteSpace(replyText))
                {
                    await _whatsApp.SendMessageAsync(fromNumber, replyText);
                    await _chatLog.LogOutboundAsync(fromNumber, working.Name, working.Email, replyText, phase);
                    await _conversationService.LogOutboundAsync(
                        Channel, fromNumber, replyText, session.Id, user.Id,
                        ct: HttpContext.RequestAborted);
                }

                // ── Persist updated flow state + user profile ─────────────────
                await PersistFlowAsync(session, working, user);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing WhatsApp webhook");
                return Ok(); // Always return 200 to Meta
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SECURITY: Verify webhook signature from Meta
        // ═══════════════════════════════════════════════════════════════════════
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

        // ═══════════════════════════════════════════════════════════════════════
        // PARSE: Extract message from Meta webhook payload
        // ═══════════════════════════════════════════════════════════════════════
        private static bool TryExtractIncomingMessage(
            string json, out string from, out string text, out string? providerMessageId)
        {
            from = string.Empty;
            text = string.Empty;
            providerMessageId = null;

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
                if (msg.TryGetProperty("id", out var id))
                    providerMessageId = id.GetString();
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

        // ═══════════════════════════════════════════════════════════════════════
        // MAIN ROUTER: Direct to correct state handler
        // ═══════════════════════════════════════════════════════════════════════
        private Task<string> ProcessMessageAsync(UserSession session, string messageText, User user, AppSession appSession)
        {
            return session.State switch
            {
                UserState.New => Task.FromResult(HandleNewUser(session)),
                UserState.AwaitingName => Task.FromResult(HandleNameInput(session, messageText)),
                UserState.AwaitingEmail => Task.FromResult(HandleEmailInput(session, messageText)),
                UserState.Verified => HandleVerifiedUserAsync(session, messageText, user, appSession),
                _ => Task.FromResult("Something went wrong. Please try again.")
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ONBOARDING FLOW
        // ═══════════════════════════════════════════════════════════════════════
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
🆘 */agent* → Talk to a human agent
🆘 */help* → Full command list";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // VERIFIED USER: Route to correct feature
        // ═══════════════════════════════════════════════════════════════════════
        private async Task<string> HandleVerifiedUserAsync(UserSession session, string messageText, User user, AppSession appSession)
        {
            var text = (messageText ?? string.Empty).Trim();
            var lower = text.ToLowerInvariant();

            // ── Active flight flow routing (numbered search/booking/cancel flow) ─
            if (session.FlightStep != FlightStep.None && lower != "/flightcancel")
            {
                // Search flow (steps 1-9)
                if (session.FlightStep >= FlightStep.ChooseAirline &&
                    session.FlightStep <= FlightStep.Confirm)
                    return await ContinueFlightSearchFlowAsync(session, text, user, appSession);

                // Booking flow (steps 10-21)
                if (session.FlightStep >= FlightStep.ViewResults &&
                    session.FlightStep <= FlightStep.BookingComplete)
                    return await ContinueBookingFlowAsync(session, text, user, appSession);

                // Cancellation flow (steps 30-31)
                if (session.FlightStep >= FlightStep.CancelConfirm)
                    return await ContinueCancelFlowAsync(session, text, user, appSession);
            }

            // ── Active free-text flight conversation (shared flight service) ──
            if (session.ConversationFlightStep != FlightConversationStep.None)
            {
                return await _flightConversation.HandleAsync(session, text, HttpContext.RequestAborted);
            }

            // ── Config shortcuts ──────────────────────────────────────────────
            var botName = _config["Bot:Name"] ?? "Femzyk_Aje_Bot";
            var company = _config["Bot:Company"] ?? "FEMZYK ENTERPRISES LTD";
            var supportEmail = _config["Bot:SupportEmail"] ?? "femzykenterprisesltd@gmail.com";

            // ── Help / Menu ───────────────────────────────────────────────────
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
• ""I want a flight from LOS to LHR"" starts a search

━━━━━━━━━━━━━━━━━━━━━━━
⚙️ *ACCOUNT*
━━━━━━━━━━━━━━━━━━━━━━━
• */status* — Your profile
• */clear* — Clear chat memory
• */reset* — Start fresh session
• */agent* — Escalate to a human agent
• */help* — This menu

━━━━━━━━━━━━━━━━━━━━━━━
📞 *SUPPORT*
━━━━━━━━━━━━━━━━━━━━━━━
📧 {supportEmail}
🏢 {company}

_Powered by Azure OpenAI + Live Data_";
            }

            // ── Status ───────────────────────────────────────────────────────
            if (lower is "/status")
            {
                return $@"📊 *Your Account*

👤 Name: {session.Name}
📧 Email: {session.Email}
📱 Phone: {session.PhoneNumber}
🕐 Session: {appSession.CreatedAtUtc:MMM dd, yyyy HH:mm} UTC
💬 Memory: {session.ConversationHistory.Count} messages
✈️ Active flow: {(session.FlightStep == FlightStep.None ? "None" : session.FlightStep.ToString())}";
            }

            // ── Clear memory ─────────────────────────────────────────────────
            if (lower is "/clear")
            {
                session.ConversationHistory.Clear();
                session.HistoryClearedAtUtc = DateTime.UtcNow; // persisted via FlowContext
                return "🧹 Memory cleared! What would you like to talk about?";
            }

            // ── Reset session ────────────────────────────────────────────────
            if (lower is "/reset" or "/restart")
            {
                session.State = UserState.New;
                session.ResetAll();
                session.ConversationFlightStep = FlightConversationStep.None;
                session.Name = null;
                session.Email = null;
                session.ConversationHistory.Clear();

                return "🔄 Your session has been reset.\n\nSend any message to start over!";
            }

            // ── /image — Generate image from prompt ─────────────────────────────
            if (lower.StartsWith("/image ") || lower.StartsWith("/generate "))
            {
                var featureEnabled = _config["FEATURE_IMAGE_GENERATION"] == "true" ||
                                     Environment.GetEnvironmentVariable("FEATURE_IMAGE_GENERATION") == "true";

                if (!featureEnabled)
                    return "🎨 Image generation is not enabled. Contact admin.";

                var prompt = text.Contains(' ') ? text[(text.IndexOf(' ') + 1)..].Trim() : "";
                if (prompt.Length < 3)
                    return "Please describe what image you want.\nExample: */image a sunset over Lagos Nigeria*";

                await _whatsApp.SendMessageAsync(session.PhoneNumber,
                    "🎨 Generating your image... Please wait (this takes 10-20 seconds)");

                var result = await _imageService.GenerateImageAsync(prompt, ct: HttpContext.RequestAborted);

                if (result.Success && !string.IsNullOrWhiteSpace(result.ImageUrl))
                {
                    await _whatsApp.SendImageAsync(session.PhoneNumber, result.ImageUrl, prompt);
                    return $"✅ Image generated! _(Model: {result.ModelUsed})_";
                }

                return "😕 Could not generate image. Please try a different prompt.";
            }

            // ── Escalate to human agent (shared service request) ─────────────
            if (lower is "/agent" or "/human" or "/support" or "/escalate")
            {
                var sr = await CreateTrackedRequestAsync(
                    "HumanEscalation", user, appSession,
                    "User requested a human agent via WhatsApp", HttpContext.RequestAborted);

                if (sr != null)
                {
                    await _serviceRequests.EscalateAsync(sr.RequestCode, "User requested agent", ct: HttpContext.RequestAborted);
                    await _auditService.LogAsync(
                        "SERVICE_REQUEST_ESCALATED", "ServiceRequest", sr.RequestCode,
                        actorId: user.Id.ToString(), channel: Channel,
                        details: "User requested agent via /agent",
                        ct: HttpContext.RequestAborted);

                    return "🆘 I've flagged your request for a human agent.\n\n" +
                           $"Reference: *{sr.RequestCode}*\n\n" +
                           "A team member will review your conversation and respond.";
                }

                return "😕 I couldn't register your request. Please try again or email support.";
            }

            // ── Cancel active flight search ──────────────────────────────────
            if (lower is "/flightcancel")
            {
                session.ResetAll();
                session.ConversationFlightStep = FlightConversationStep.None;
                return "✈️ Flight search cancelled.\n\nType */flight* to start a new search.";
            }

            // ── Start flight search (numbered flow) ──────────────────────────
            if (lower is "/flight" or "/flights")
            {
                session.ResetAll();
                session.FlightStep = FlightStep.ChooseAirline;

                var airlines = _scrapingOpt.Airlines ?? new List<AirlineTarget>();
                if (airlines.Count == 0)
                    return "⚠️ No airlines configured. Please contact support.";

                var list = string.Join("\n", airlines.Select((a, i) =>
                    $"{i + 1}. *{a.Name}* ({a.SourceKey})"));

                return $@"✈️ *Flight Search*

Choose an airline by replying with a number or source key:

{list}

Or type */flightcancel* to abort.";
            }

            // ── My bookings ──────────────────────────────────────────────────
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

            // ── Start cancellation flow ──────────────────────────────────────
            if (lower is "/cancelbooking" or "/cancel" or "/cancelreservation")
            {
                session.FlightStep = FlightStep.CancelConfirm;
                return "🚫 *Cancel Booking*\n\nEnter your *Reservation Code* (e.g., FZK-260410-1234):";
            }

            // ── Products ─────────────────────────────────────────────────────
            if (lower is "/products")
            {
                var items = await _catalog.GetLatestProductsAsync(10);
                if (items.Count == 0) return "No products found yet. (Catalog is empty)";

                var lines = items.Select(p =>
                    $"• *{p.Name}* ({p.Sku}) — {p.Currency} {p.Price:N0} {(p.StockQty > 0 ? "✅" : "⚠️ Out of stock")}");

                return "🛍️ *Latest Products*\n\n" + string.Join("\n", lines) +
                       "\n\nUse */product <SKU>* to view details.";
            }

            // ── Search products ───────────────────────────────────────────────
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

            // ── Product detail ────────────────────────────────────────────────
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

            // ── Fallback: shared intent router → flight service or AI ────────
            var route = _intentRouter.Route(text, flightFlowActive: false);

            if (route == MessageRoute.FlightService)
            {
                // Start the shared free-text flight conversation
                var sr = await CreateTrackedRequestAsync(
                    "FlightSearch", user, appSession,
                    $"Free-text flight request: {Truncate(text, 200)}", HttpContext.RequestAborted);

                var reply = await _flightConversation.StartAsync(session);

                await _auditService.LogAsync(
                    "FLIGHT_FLOW_STARTED", "Session", appSession.SessionId,
                    actorId: user.Id.ToString(), channel: Channel,
                    details: "Intent router → flight service",
                    ct: HttpContext.RequestAborted);

                // Keep the tracking request alive across the conversation; it is
                // completed when the quote is produced (FlightConversationService
                // is stateless per call, so the SR is completed here as 'processing').
                if (sr != null)
                    await _serviceRequests.UpdateStatusAsync(
                        sr.RequestCode, ServiceRequestStatus.Processing, "Flight conversation started",
                        ct: HttpContext.RequestAborted);

                return reply;
            }

            // General AI assistant (resilient: falls back to deterministic commands)
            try
            {
                var sr = await CreateTrackedRequestAsync(
                    "AiAssistance", user, appSession,
                    $"AI query: {Truncate(text, 200)}", HttpContext.RequestAborted);

                var response = await _llm.GetResponseAsync(session, text);

                await CompleteTrackedRequestAsync(sr, "response_delivered", "AI response delivered", HttpContext.RequestAborted);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM error for {Phone}", session.PhoneNumber);
                return "😕 I'm having trouble right now. Try again in a moment.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // FLIGHT SEARCH FLOW (Steps 1-9)
        // ═══════════════════════════════════════════════════════════════════════
        private async Task<string> ContinueFlightSearchFlowAsync(UserSession session, string text, User user, AppSession appSession)
        {
            var airlines = _scrapingOpt.Airlines ?? new List<AirlineTarget>();

            switch (session.FlightStep)
            {
                case FlightStep.ChooseAirline:
                    AirlineTarget? chosen = null;
                    if (int.TryParse(text.Trim(), out var idx) && idx >= 1 && idx <= airlines.Count)
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
                        return "Invalid date. Use YYYY-MM-DD (e.g., 2026-02-20).";
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

                    // Service request for this search (audited lifecycle)
                    var sr = await CreateTrackedRequestAsync(
                        "FlightSearch", user, appSession,
                        $"{target.SourceKey} {session.FlightDraft.From}→{session.FlightDraft.To} {session.FlightDraft.DepartDate:yyyy-MM-dd}",
                        HttpContext.RequestAborted);
                    if (sr != null)
                        await _serviceRequests.UpdateStatusAsync(
                            sr.RequestCode, ServiceRequestStatus.Processing, "Searching flights",
                            ct: HttpContext.RequestAborted);

                    await _whatsApp.SendMessageAsync(session.PhoneNumber,
                        "🔍 Searching for the best prices... please wait.");

                    FlightQuote quote;
                    try
                    {
                        quote = await _flightPricing.GetQuoteAsync(
                            target, session.FlightDraft, session.FlightPricingMode, HttpContext.RequestAborted);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Flight pricing failed");
                        await FailTrackedRequestAsync(sr, "Pricing provider error", HttpContext.RequestAborted);
                        session.ResetAll();
                        return "😕 Flight search failed. Please try again or use */flightcancel*.";
                    }

                    session.CurrentQuote = quote;
                    session.FlightStep = FlightStep.ViewResults;

                    var searchResult = JsonSerializer.Serialize(new
                    {
                        airline = target.SourceKey,
                        from = session.FlightDraft.From,
                        to = session.FlightDraft.To,
                        price = quote.Price,
                        currency = quote.Currency,
                        isPriceExact = quote.IsPriceExact,
                        bookingUrl = quote.BookingUrl
                    });
                    await CompleteTrackedRequestAsync(sr, searchResult, "Quote produced", HttpContext.RequestAborted);

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

        // ═══════════════════════════════════════════════════════════════════════
        // BOOKING FLOW — Passenger collection (Steps 10-21)
        // ═══════════════════════════════════════════════════════════════════════
        private async Task<string> ContinueBookingFlowAsync(UserSession session, string text, User user, AppSession appSession)
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
                        return "Invalid date. Use YYYY-MM-DD (e.g., 2028-05-15).";
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

                    // Service request for the booking (audited lifecycle)
                    var sr = await CreateTrackedRequestAsync(
                        "FlightBooking", user, appSession,
                        $"Booking {airline.SourceKey} {session.FlightDraft.From}→{session.FlightDraft.To} {session.FlightDraft.DepartDate:yyyy-MM-dd}",
                        HttpContext.RequestAborted);
                    if (sr != null)
                        await _serviceRequests.UpdateStatusAsync(
                            sr.RequestCode, ServiceRequestStatus.Processing, "Creating reservation",
                            ct: HttpContext.RequestAborted);

                    try
                    {
                        // Create reservation in database
                        var reservation = await _reservations.CreateReservationAsync(
                            session.PhoneNumber,
                            session.Name,
                            session.PassengerDraft.Email,
                            session.FlightDraft,
                            session.CurrentQuote!,
                            airline.Name,
                            HttpContext.RequestAborted);

                        session.ActiveReservationCode = reservation.ReservationCode;

                        if (sr != null)
                        {
                            // Link the service request to the reservation (auditable)
                            var tracked = await _serviceRequests.GetByCodeAsync(sr.RequestCode, HttpContext.RequestAborted);
                            if (tracked != null)
                            {
                                tracked.ReservationCode = reservation.ReservationCode;
                                await _auditService.LogAsync(
                                    "SERVICE_REQUEST_UPDATED", "ServiceRequest", tracked.RequestCode,
                                    actorId: "system", channel: Channel,
                                    details: $"Linked reservation {reservation.ReservationCode}",
                                    ct: HttpContext.RequestAborted);
                            }
                        }

                        // Attach passenger details
                        await _reservations.AttachPassengerAsync(
                            reservation.ReservationCode,
                            session.PassengerDraft,
                            HttpContext.RequestAborted);

                        // Fire automation in background (non-blocking; orchestrator
                        // creates its own DI scope so this is safe off-request)
                        _ = Task.Run(() => _orchestrator.ExecuteBookingAsync(
                            reservation.ReservationCode,
                            session.FlightDraft,
                            session.PassengerDraft,
                            CancellationToken.None), CancellationToken.None);

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
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Reservation creation failed");
                        await FailTrackedRequestAsync(sr, "Reservation creation failed", HttpContext.RequestAborted);
                        session.ResetAll();
                        return "😕 I couldn't create your reservation. Please try again or contact support.";
                    }

                default:
                    session.ResetAll();
                    return "Booking flow error. Type */flight* to restart.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CANCELLATION FLOW (Steps 30-31)
        // ═══════════════════════════════════════════════════════════════════════
        private async Task<string> ContinueCancelFlowAsync(UserSession session, string text, User user, AppSession appSession)
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

                    var sr = await CreateTrackedRequestAsync(
                        "BookingCancellation", user, appSession,
                        $"Cancel reservation {session.PendingCancellationCode}",
                        HttpContext.RequestAborted);
                    if (sr != null)
                    {
                        var tracked = await _serviceRequests.GetByCodeAsync(sr.RequestCode, HttpContext.RequestAborted);
                        if (tracked != null)
                        {
                            tracked.ReservationCode = session.PendingCancellationCode;
                        }
                        await _serviceRequests.UpdateStatusAsync(
                            sr.RequestCode, ServiceRequestStatus.Processing, "Cancelling",
                            ct: HttpContext.RequestAborted);
                    }

                    var success = await _orchestrator.ExecuteCancellationAsync(
                        session.PendingCancellationCode!,
                        "User requested cancellation via WhatsApp",
                        HttpContext.RequestAborted);

                    session.ResetAll();

                    if (success)
                    {
                        await CompleteTrackedRequestAsync(sr, "cancelled", "Reservation cancelled", HttpContext.RequestAborted);
                        return @"✅ *Reservation Successfully Cancelled!*

Your booking has been cancelled. Any applicable refund will be processed within 5-10 business days.

Type */flight* to book a new flight.";
                    }

                    await FailTrackedRequestAsync(sr, "Cancellation failed", HttpContext.RequestAborted);
                    return @"❌ *Cancellation Failed*

We couldn't automatically cancel this reservation. Please contact support:
📧 femzykenterprisesltd@gmail.com";

                default:
                    session.ResetAll();
                    return "Type */cancelbooking* to try again.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PERSISTENCE HELPERS (shared platform state)
        // ═══════════════════════════════════════════════════════════════════════
        /// <summary>
        /// Loads recent session messages from the shared message store and maps
        /// them to the LLM prompt history (user/assistant roles).
        /// </summary>
        private async Task<List<ChatMessage>> LoadPromptHistoryAsync(int sessionId, string? currentProviderMessageId, DateTime? historyClearedAt = null)
        {
            var history = new List<ChatMessage>();
            try
            {
                var dbMessages = await _conversationService.GetSessionMessagesAsync(sessionId, 20, HttpContext.RequestAborted);

                foreach (var m in dbMessages
                             .Where(x => x.Content != null)
                             .Where(x => historyClearedAt == null || x.Timestamp > historyClearedAt)
                             .OrderBy(x => x.Timestamp))
                {
                    // Skip the just-received inbound message (the LLM prompt appends it)
                    if (m.Direction == "inbound" &&
                        !string.IsNullOrWhiteSpace(currentProviderMessageId) &&
                        m.ProviderMessageId == currentProviderMessageId)
                        continue;

                    if (m.Direction == "inbound")
                        history.Add(new ChatMessage { Role = "user", Content = m.Content });
                    else
                        history.Add(new ChatMessage { Role = "assistant", Content = m.Content });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load prompt history for session {SessionId}", sessionId);
            }

            return history;
        }

        /// <summary>
        /// Persists the working session state back to the database-backed
        /// AppSession (state machine + flight flow context) and to the User
        /// profile (name/email collected during onboarding).
        /// </summary>
        private async Task PersistFlowAsync(AppSession session, UserSession working, User user)
        {
            var ct = HttpContext.RequestAborted;
            try
            {
                // User profile sync (onboarding collected name/email)
                if (working.State >= UserState.AwaitingName)
                {
                    var nameChanged = working.Name != null && !string.Equals(working.Name, user.DisplayName, StringComparison.Ordinal);
                    var emailChanged = working.Email != null && !string.Equals(working.Email, user.Email, StringComparison.OrdinalIgnoreCase);
                    if (nameChanged || emailChanged)
                    {
                        user = await _userService.UpdateProfileAsync(
                            user.Id,
                            nameChanged ? working.Name : null,
                            emailChanged ? working.Email : null,
                            ct);
                    }
                }

                // Session state + serialized flow context
                var flow = FlowContext.FromUserSession(working);
                session.CurrentState = flow.State.ToString();
                session.WorkflowStep = flow.FlightStep != FlightStep.None
                    ? flow.FlightStep.ToString()
                    : flow.ConversationFlightStep != FlightConversationStep.None
                        ? flow.ConversationFlightStep.ToString()
                        : null;
                session.ContextData = flow.ToJson();

                await _sessionService.UpdateSessionAsync(session, ct);

                await _userService.TouchActivityAsync(user.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist flow state for session {SessionId}", session.SessionId);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SERVICE REQUEST HELPERS (audited lifecycle)
        // ═══════════════════════════════════════════════════════════════════════
        private async Task<ServiceRequest?> CreateTrackedRequestAsync(
            string requestType, User user, AppSession session, string? summary, CancellationToken ct)
        {
            try
            {
                var sr = await _serviceRequests.CreateAsync(
                    requestType, Channel, user.Id, session.Id, summary, ct);

                await _auditService.LogAsync(
                    "SERVICE_REQUEST_CREATED", "ServiceRequest", sr.RequestCode,
                    actorId: user.Id.ToString(), channel: Channel,
                    details: requestType, ct: ct);

                return sr;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create service request ({Type}) — continuing without tracking", requestType);
                return null;
            }
        }

        private async Task CompleteTrackedRequestAsync(
            ServiceRequest? sr, string? resultData, string? statusMessage, CancellationToken ct)
        {
            if (sr == null) return;
            try
            {
                await _serviceRequests.CompleteAsync(sr.RequestCode, resultData, ct);
                await _auditService.LogAsync(
                    "SERVICE_REQUEST_COMPLETED", "ServiceRequest", sr.RequestCode,
                    actorId: "system", channel: Channel,
                    details: statusMessage, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to complete service request {Code}", sr.RequestCode);
            }
        }

        private async Task FailTrackedRequestAsync(
            ServiceRequest? sr, string? reason, CancellationToken ct)
        {
            if (sr == null) return;
            try
            {
                await _serviceRequests.UpdateStatusAsync(sr.RequestCode, ServiceRequestStatus.Failed, reason, ct);
                await _auditService.LogAsync(
                    "SERVICE_REQUEST_FAILED", "ServiceRequest", sr.RequestCode,
                    actorId: "system", channel: Channel,
                    details: reason, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark service request {Code} as failed", sr.RequestCode);
            }
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max] + "…";

        // ═══════════════════════════════════════════════════════════════════════
        // HELPER METHODS
        // ═══════════════════════════════════════════════════════════════════════
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

    // ═══════════════════════════════════════════════════════════════════════════
    // FLOW CONTEXT — persisted JSON state of the in-flight conversation
    // ═══════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Serializable snapshot of the working UserSession that is persisted to
    /// AppSession.ContextData so flight flows survive restarts and reconnects.
    /// </summary>
    public sealed class FlowContext
    {
        public UserState State { get; set; } = UserState.New;
        public string? Name { get; set; }
        public string? Email { get; set; }
        public FlightStep FlightStep { get; set; } = FlightStep.None;
        public FlightPricingMode FlightPricingMode { get; set; } = FlightPricingMode.Auto;
        public FlightConversationStep ConversationFlightStep { get; set; } = FlightConversationStep.None;
        public FlightSearchDraft FlightDraft { get; set; } = new();
        public FlightQuote? CurrentQuote { get; set; }
        public PassengerInfo PassengerDraft { get; set; } = new();
        public string? ActiveReservationCode { get; set; }
        public string? PendingCancellationCode { get; set; }

        /// <summary>When the user cleared memory; prompt history before this is ignored.</summary>
        public DateTime? HistoryClearedAtUtc { get; set; }

        public static FlowContext FromUserSession(UserSession s) => new()
        {
            State = s.State,
            Name = s.Name,
            Email = s.Email,
            FlightStep = s.FlightStep,
            FlightPricingMode = s.FlightPricingMode,
            ConversationFlightStep = s.ConversationFlightStep,
            FlightDraft = s.FlightDraft,
            CurrentQuote = s.CurrentQuote,
            PassengerDraft = s.PassengerDraft,
            ActiveReservationCode = s.ActiveReservationCode,
            PendingCancellationCode = s.PendingCancellationCode,
            HistoryClearedAtUtc = s.HistoryClearedAtUtc
        };

        public UserSession ToUserSession(string phoneNumber) => new()
        {
            PhoneNumber = phoneNumber,
            Name = Name,
            Email = Email,
            State = State,
            FlightStep = FlightStep,
            FlightPricingMode = FlightPricingMode,
            ConversationFlightStep = ConversationFlightStep,
            FlightDraft = FlightDraft ?? new FlightSearchDraft(),
            CurrentQuote = CurrentQuote,
            PassengerDraft = PassengerDraft ?? new PassengerInfo(),
            ActiveReservationCode = ActiveReservationCode,
            PendingCancellationCode = PendingCancellationCode,
            HistoryClearedAtUtc = HistoryClearedAtUtc
        };

        public string ToJson() => JsonSerializer.Serialize(this);

        public static FlowContext FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new FlowContext();
            try
            {
                return JsonSerializer.Deserialize<FlowContext>(json) ?? new FlowContext();
            }
            catch
            {
                // Corrupt context data: start fresh rather than crashing the user
                return new FlowContext();
            }
        }
    }
}
