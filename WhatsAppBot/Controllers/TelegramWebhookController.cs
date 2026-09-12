using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Flights;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Controllers
{
    /// <summary>
    /// Handles incoming Telegram Bot API webhook updates.
    ///
    /// Uses the SAME shared application services as the WhatsApp channel
    /// (User / PersistentSession / Conversation / ServiceRequest / Audit /
    /// IntentRouter / FlightConversation / LLM). Business rules are
    /// centralized — this adapter only does Telegram parsing/presentation.
    /// </summary>
    [ApiController]
    [Route("telegram")]
    [Route("api/telegram")]
    public class TelegramWebhookController : ControllerBase
    {
        private readonly ITelegramService _telegram;
        private readonly IPersistentSessionService _sessionService;
        private readonly IConversationService _conversationService;
        private readonly IUserService _userService;
        private readonly IServiceRequestService _serviceRequestService;
        private readonly ILLMService _llm;
        private readonly IAuditService _auditService;
        private readonly IIntentRouter _intentRouter;
        private readonly FlightConversationService _flightConversation;
        private readonly IConfiguration _config;
        private readonly ILogger<TelegramWebhookController> _logger;

        private const string Channel = "telegram";

        [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
        private static partial Regex EmailRegex();

        public TelegramWebhookController(
            ITelegramService telegram,
            IPersistentSessionService sessionService,
            IConversationService conversationService,
            IUserService userService,
            IServiceRequestService serviceRequestService,
            ILLMService llm,
            IAuditService auditService,
            IIntentRouter intentRouter,
            FlightConversationService flightConversation,
            IConfiguration config,
            ILogger<TelegramWebhookController> logger)
        {
            _telegram = telegram;
            _sessionService = sessionService;
            _conversationService = conversationService;
            _userService = userService;
            _serviceRequestService = serviceRequestService;
            _llm = llm;
            _auditService = auditService;
            _intentRouter = intentRouter;
            _flightConversation = flightConversation;
            _config = config;
            _logger = logger;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TELEGRAM WEBHOOK ENDPOINT
        // ═══════════════════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            try
            {
                // Verify webhook secret if configured
                if (!VerifyWebhookSecret())
                {
                    _logger.LogWarning("Telegram webhook secret verification failed");
                    await _auditService.LogAsync(
                        "WEBHOOK_REJECTED", "Webhook", null,
                        actorId: "system", channel: Channel,
                        details: "Invalid X-Telegram-Bot-Api-Secret-Token",
                        ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                        ct: HttpContext.RequestAborted);
                    return Unauthorized();
                }

                Request.EnableBuffering();
                string rawBody;
                using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
                {
                    rawBody = await reader.ReadToEndAsync();
                    Request.Body.Position = 0;
                }

                if (string.IsNullOrWhiteSpace(rawBody))
                    return Ok();

                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;

                // ── Message update ──────────────────────────────────────────
                if (root.TryGetProperty("message", out var message))
                {
                    await ProcessMessageAsync(message);
                }
                else if (root.TryGetProperty("callback_query", out var callback))
                {
                    await ProcessCallbackQueryAsync(callback);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Telegram webhook");
                return Ok(); // Always return 200 to Telegram
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MESSAGE PROCESSING
        // ═══════════════════════════════════════════════════════════════════════
        private async Task ProcessMessageAsync(JsonElement message)
        {
            var ct = HttpContext.RequestAborted;

            // Extract chat and user info
            var chatId = message.GetProperty("chat").GetProperty("id").GetInt64().ToString();
            var chatType = message.GetProperty("chat").GetProperty("type").GetString() ?? "private";

            // Only process private messages
            if (chatType != "private")
            {
                _logger.LogDebug("Ignoring non-private Telegram message from chat {ChatId}", chatId);
                return;
            }

            var from = message.GetProperty("from");
            var userId = from.GetProperty("id").GetInt64().ToString();
            var firstName = from.TryGetProperty("first_name", out var fn) ? fn.GetString() : null;
            var lastName = from.TryGetProperty("last_name", out var ln) ? ln.GetString() : null;
            var displayName = $"{firstName} {lastName}".Trim();
            var providerMessageId = message.TryGetProperty("message_id", out var mid) ? mid.GetInt64().ToString() : null;

            // Extract message text
            string messageText = string.Empty;
            if (message.TryGetProperty("text", out var textElement))
            {
                messageText = textElement.GetString() ?? string.Empty;
            }

            messageText = messageText.Trim();
            if (string.IsNullOrWhiteSpace(messageText))
            {
                _logger.LogDebug("Ignoring non-text Telegram message from {UserId}", userId);
                return;
            }

            _logger.LogInformation("Telegram message from {UserId} ({Name}): {Preview}",
                userId, displayName,
                messageText.Length > 60 ? $"{messageText[..60]}..." : messageText);

            // ── Idempotency: same update/message twice → one business action ─
            if (!string.IsNullOrWhiteSpace(providerMessageId) &&
                await _conversationService.ExistsByProviderMessageIdAsync(providerMessageId, ct))
            {
                _logger.LogWarning("Duplicate Telegram message ignored: {MsgId}", providerMessageId);
                await _auditService.LogAsync(
                    "WEBHOOK_DUPLICATE", "Message", providerMessageId,
                    actorId: "system", channel: Channel,
                    details: $"provider user {userId}",
                    ct: ct);
                return;
            }

            // Find or create user (shared user across channels)
            var (user, _identity, created) = await _userService.FindOrCreateByChannelAsync(
                Channel, userId, displayName, ct);

            if (created)
            {
                await _auditService.LogAsync(
                    "USER_CREATED", "User", user.Id.ToString(),
                    actorId: "system", channel: Channel,
                    details: $"provider user {userId}",
                    ct: ct);
            }

            // Get or create persistent session
            var session = await _sessionService.GetOrCreateSessionAsync(
                Channel, userId, user.Id, ct);

            // Log inbound message (shared message store)
            var inbound = await _conversationService.LogInboundAsync(
                Channel, userId, messageText, providerMessageId,
                session.Id, user.Id, ct: ct);

            await _auditService.LogAsync(
                "WEBHOOK_RECEIVED", "Message", inbound.Id.ToString(),
                actorId: userId, channel: Channel,
                details: "inbound message",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                ct: ct);

            // Restore persisted flow state (same FlowContext as WhatsApp)
            var flow = FlowContext.FromJson(session.ContextData);
            var working = flow.ToUserSession(user.PhoneNumber ?? $"tg_{user.Id}");
            working.Name = user.DisplayName;
            working.Email = user.Email;

            // Process the message and generate response (shared business rules)
            var replyText = await ProcessCommandAsync(session, user, working, messageText, ct);

            if (!string.IsNullOrWhiteSpace(replyText))
            {
                await _telegram.SendMessageAsync(chatId, replyText, ct);

                await _conversationService.LogOutboundAsync(
                    Channel, userId, replyText, session.Id, user.Id, ct: ct);
            }

            // Persist updated flow state (mirrors WhatsApp behaviour)
            await PersistFlowAsync(session, working, user, ct);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // COMMAND PROCESSING (shared logic with WhatsApp — same state machine)
        // ═══════════════════════════════════════════════════════════════════════
        private async Task<string> ProcessCommandAsync(
            AppSession session, User user, UserSession working, string messageText, CancellationToken ct)
        {
            var text = (messageText ?? "").Trim();
            var lower = text.ToLowerInvariant();

            // ── Active free-text flight conversation (shared flight service) ─
            if (working.ConversationFlightStep != FlightConversationStep.None)
            {
                return await _flightConversation.HandleAsync(working, text, ct);
            }

            // ── Onboarding (same state names as WhatsApp) ──────────────────
            switch (working.State)
            {
                case UserState.New:
                    working.State = UserState.AwaitingName;
                    return "👋 Welcome! I'm your AI travel assistant.\n\nPlease reply with your *full name* to get started.";

                case UserState.AwaitingName:
                    if (text.Length < 2)
                        return "Please enter a valid name (at least 2 characters).";
                    if (text.Length > 100)
                        return "That name is too long. Please enter a shorter name.";

                    await _userService.UpdateProfileAsync(user.Id, text, null, ct);
                    working.Name = text;
                    working.State = UserState.AwaitingEmail;
                    return $"Nice to meet you, *{text}*! 🎉\n\nNow, please enter your *email address*.";

                case UserState.AwaitingEmail:
                    if (!EmailRegex().IsMatch(text))
                        return "That doesn't look like a valid email. Please try again.";

                    await _userService.UpdateProfileAsync(user.Id, null, text.ToLowerInvariant(), ct);
                    working.Email = text.ToLowerInvariant();
                    working.State = UserState.Verified;

                    return "✅ *You're all set!*\n\n" +
                           "Here's what I can do:\n\n" +
                           "✈️ */flight* — Search & book flights\n" +
                           "📋 */mybookings* — View reservations\n" +
                           "🛍️ */products* — Browse products\n" +
                           "💬 Just type anything to chat with AI!\n" +
                           "🆘 */agent* — Escalate to a human agent\n" +
                           "🆘 */help* — Full command list";
            }

            // ── Handle commands for verified users ──────────────────────────
            if (lower is "/start")
            {
                working.State = UserState.New;
                return "👋 Welcome back! Send any message to get started.";
            }

            if (lower is "/help" or "/menu" or "help" or "menu")
            {
                return "🤖 *Command Center*\n\n" +
                       "━━━━━━━━━━━━━━━━━\n" +
                       "✈️ *FLIGHTS*\n" +
                       "• */flight* — Search & book flights\n" +
                       "• */mybookings* — View reservations\n" +
                       "• */cancelbooking* — Cancel a booking\n\n" +
                       "🛍️ *PRODUCTS*\n" +
                       "• */products* — Browse catalog\n" +
                       "• */search <keyword>* — Search items\n\n" +
                       "💬 *AI*\n" +
                       "• Type anything for AI assistance\n" +
                       "• \"I want a flight from LOS to LHR\" starts a search\n\n" +
                       "⚙️ *ACCOUNT*\n" +
                       "• */status* — Your profile\n" +
                       "• */reset* — Start fresh\n" +
                       "• */agent* — Escalate to a human agent\n" +
                       "• */help* — This menu";
            }

            if (lower is "/status")
            {
                return $"📊 *Your Account*\n\n" +
                       $"👤 Name: {user.DisplayName}\n" +
                       $"📧 Email: {user.Email}\n" +
                       $"📱 Channel: Telegram\n" +
                       $"🕐 Session: {session.CreatedAtUtc:MMM dd, yyyy HH:mm} UTC\n" +
                       $"✈️ State: {working.State}";
            }

            if (lower is "/reset" or "/restart")
            {
                working.State = UserState.New;
                working.ResetAll();
                working.Name = null;
                working.Email = null;
                return "🔄 Session reset!\n\nSend any message to start over.";
            }

            if (lower is "/cancel")
            {
                working.ConversationFlightStep = FlightConversationStep.None;
                working.ResetAll();
                return "❌ Current action cancelled.\n\nType */help* to see what I can do.";
            }

            if (lower is "/agent" or "/human" or "/support" or "/escalate")
            {
                var sr = await _serviceRequestService.CreateAsync(
                    "HumanEscalation", Channel, user.Id, session.Id,
                    "User requested human agent via Telegram", ct);

                await _serviceRequestService.EscalateAsync(sr.RequestCode, "User requested agent", ct: ct);
                await _auditService.LogAsync("SERVICE_REQUEST_ESCALATED", "ServiceRequest", sr.RequestCode,
                    user.Id.ToString(), Channel, details: "User requested agent via /agent", ct: ct);

                return "🆘 I've flagged your request for a human agent.\n\n" +
                       $"Reference: *{sr.RequestCode}*\n\n" +
                       "A team member will review your conversation and respond.";
            }

            if (lower is "/flight" or "/flights")
            {
                // Start the shared free-text flight conversation
                var sr = await _serviceRequestService.CreateAsync(
                    "FlightSearch", Channel, user.Id, session.Id,
                    "Flight search started via /flight command", ct);
                await _serviceRequestService.UpdateStatusAsync(
                    sr.RequestCode, ServiceRequestStatus.Processing, "Flight conversation started", ct: ct);
                await _auditService.LogAsync("SERVICE_REQUEST_CREATED", "ServiceRequest", sr.RequestCode,
                    user.Id.ToString(), Channel, details: "FlightSearch", ct: ct);

                return await _flightConversation.StartAsync(working);
            }

            if (lower is "/mybookings" or "/bookings" or "/reservations")
            {
                return "📋 View your reservations:\n\n" +
                       "Please use WhatsApp for full booking management.\n" +
                       "Or describe your booking and I'll assist!";
            }

            if (lower is "/products")
            {
                return "🛍️ Browse our product catalog!\n\n" +
                       "Type */search <keyword>* to find specific items.\n" +
                       "Or ask me about products using natural language!";
            }

            if (lower.StartsWith("/search "))
            {
                var query = text[8..].Trim();
                return $"🔎 Searching for: *{query}*\n\nI'll look that up for you! (Search via AI)";
            }

            // ── Fallback: shared intent router → flight service or AI ───────
            var route = _intentRouter.Route(text, flightFlowActive: false);

            if (route == MessageRoute.FlightService)
            {
                var sr = await _serviceRequestService.CreateAsync(
                    "FlightSearch", Channel, user.Id, session.Id,
                    $"Free-text flight request: {Truncate(text, 200)}", ct);
                await _serviceRequestService.UpdateStatusAsync(
                    sr.RequestCode, ServiceRequestStatus.Processing, "Flight conversation started", ct: ct);
                await _auditService.LogAsync("SERVICE_REQUEST_CREATED", "ServiceRequest", sr.RequestCode,
                    user.Id.ToString(), Channel, details: "FlightSearch (intent router)", ct: ct);

                return await _flightConversation.StartAsync(working);
            }

            // General AI assistant (resilient: deterministic fallback when AI is down)
            try
            {
                var sr = await _serviceRequestService.CreateAsync(
                    "AiAssistance", Channel, user.Id, session.Id,
                    $"AI query: {Truncate(text, 200)}", ct);

                var response = await _llm.GetResponseAsync(working, messageText);

                await _serviceRequestService.CompleteAsync(sr.RequestCode, "response_delivered", ct: ct);
                await _auditService.LogAsync("SERVICE_REQUEST_COMPLETED", "ServiceRequest", sr.RequestCode,
                    actorId: "system", channel: Channel, details: "AI response delivered", ct: ct);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI response failed for Telegram user {UserId}", user.Id);
                return "😕 I'm having trouble right now. Try again or use */help* for commands.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CALLBACK QUERY PROCESSING
        // ═══════════════════════════════════════════════════════════════════════
        private Task ProcessCallbackQueryAsync(JsonElement callback)
        {
            var callbackId = callback.TryGetProperty("id", out var cid) ? cid.GetString() : null;
            var data = callback.TryGetProperty("data", out var d) ? d.GetString() : null;

            _logger.LogInformation("Telegram callback query: {Data}", data);

            // Future: handle inline keyboard callbacks
            return Task.CompletedTask;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PERSISTENCE HELPERS
        // ═══════════════════════════════════════════════════════════════════════
        private async Task PersistFlowAsync(AppSession session, UserSession working, User user, CancellationToken ct)
        {
            try
            {
                var flow = FlowContext.FromUserSession(working);
                session.CurrentState = flow.State.ToString();
                session.WorkflowStep = flow.ConversationFlightStep != FlightConversationStep.None
                    ? flow.ConversationFlightStep.ToString()
                    : null;
                session.ContextData = flow.ToJson();

                await _sessionService.UpdateSessionAsync(session, ct);
                await _userService.TouchActivityAsync(user.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist Telegram flow state for session {SessionId}", session.SessionId);
            }
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max] + "…";

        // ═══════════════════════════════════════════════════════════════════════
        // SECURITY
        // ═══════════════════════════════════════════════════════════════════════
        private bool VerifyWebhookSecret()
        {
            var configuredSecret = _config["Telegram:WebhookSecret"]
                ?? Environment.GetEnvironmentVariable("TELEGRAM_WEBHOOK_SECRET");

            // If no secret is configured, allow in dev mode
            if (string.IsNullOrWhiteSpace(configuredSecret))
                return true;

            var headerSecret = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
            return string.Equals(headerSecret, configuredSecret, StringComparison.Ordinal);
        }
    }
}
