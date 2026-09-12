using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Controllers
{
    /// <summary>
    /// Handles incoming Telegram Bot API webhook updates.
    /// Uses the same underlying business logic as WhatsApp through shared services.
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
        private readonly IConfiguration _config;
        private readonly ILogger<TelegramWebhookController> _logger;

        private const string Channel = "telegram";

        public TelegramWebhookController(
            ITelegramService telegram,
            IPersistentSessionService sessionService,
            IConversationService conversationService,
            IUserService userService,
            IServiceRequestService serviceRequestService,
            ILLMService llm,
            IAuditService auditService,
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
            _config = config;
            _logger = logger;
        }

        // ═══════════════════════════════════════════════════════════════════
        // TELEGRAM WEBHOOK ENDPOINT
        // ═══════════════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            try
            {
                // Verify webhook secret if configured
                if (!VerifyWebhookSecret())
                {
                    _logger.LogWarning("Telegram webhook secret verification failed");
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

                // Extract update_id for idempotency
                var updateId = root.TryGetProperty("update_id", out var uid) ? uid.GetInt64() : 0;

                // Process message
                if (root.TryGetProperty("message", out var message))
                {
                    await ProcessMessageAsync(message, updateId);
                }
                else if (root.TryGetProperty("callback_query", out var callback))
                {
                    await ProcessCallbackQueryAsync(callback, updateId);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Telegram webhook");
                return Ok(); // Always return 200 to Telegram
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // MESSAGE PROCESSING
        // ═══════════════════════════════════════════════════════════════════
        private async Task ProcessMessageAsync(JsonElement message, long updateId)
        {
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

            if (string.IsNullOrWhiteSpace(messageText))
            {
                _logger.LogDebug("Ignoring non-text Telegram message from {UserId}", userId);
                return;
            }

            _logger.LogInformation("📨 Telegram message from {UserId} ({Name}): {Preview}",
                userId, displayName,
                messageText.Length > 60 ? $"{messageText[..60]}..." : messageText);

            // Find or create user
            var (user, identity) = await _userService.FindOrCreateByChannelAsync(
                Channel, userId, displayName);

            // Get or create persistent session
            var session = await _sessionService.GetOrCreateSessionAsync(
                Channel, userId, user.Id);

            // Log inbound message (with idempotency)
            await _conversationService.LogInboundAsync(
                Channel, userId, messageText, providerMessageId,
                session.Id, user.Id);

            // Process the message and generate response
            var replyText = await ProcessCommandAsync(session, user, messageText);

            if (!string.IsNullOrWhiteSpace(replyText))
            {
                await _telegram.SendMessageAsync(chatId, replyText);

                await _conversationService.LogOutboundAsync(
                    Channel, userId, replyText, session.Id, user.Id);
            }

            // Update session
            await _sessionService.UpdateSessionAsync(session);
        }

        // ═══════════════════════════════════════════════════════════════════
        // COMMAND PROCESSING (shared logic with WhatsApp)
        // ═══════════════════════════════════════════════════════════════════
        private async Task<string> ProcessCommandAsync(AppSession session, User user, string messageText)
        {
            var text = (messageText ?? "").Trim();
            var lower = text.ToLowerInvariant();

            // Handle based on current session state
            switch (session.CurrentState)
            {
                case "New":
                    session.CurrentState = "Onboarding_Name";
                    await _sessionService.UpdateSessionAsync(session);
                    return $"👋 Welcome! I'm your AI travel assistant.\n\nPlease reply with your *full name* to get started.";

                case "Onboarding_Name":
                    if (text.Length < 2)
                        return "Please enter a valid name (at least 2 characters).";

                    await _userService.UpdateProfileAsync(user.Id, text, null);
                    session.CurrentState = "Onboarding_Email";
                    await _sessionService.UpdateSessionAsync(session);
                    return $"Nice to meet you, *{text}*! 🎉\n\nNow, please enter your *email address*.";

                case "Onboarding_Email":
                    if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$"))
                        return "That doesn't look like a valid email. Please try again.";

                    await _userService.UpdateProfileAsync(user.Id, null, text.ToLowerInvariant());
                    session.CurrentState = "Verified";
                    await _sessionService.UpdateSessionAsync(session);

                    return $"✅ *You're all set!*\n\n" +
                           "Here's what I can do:\n\n" +
                           "✈️ */flight* — Search & book flights\n" +
                           "📋 */mybookings* — View reservations\n" +
                           "🛍️ */products* — Browse products\n" +
                           "💬 Just type anything to chat with AI!\n" +
                           "🆘 */help* — Full command list";
            }

            // ── Handle commands for verified users ──────────────────────
            if (lower is "/start")
            {
                session.CurrentState = "New";
                await _sessionService.UpdateSessionAsync(session);
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
                       "• Type anything for AI assistance\n\n" +
                       "⚙️ *ACCOUNT*\n" +
                       "• */status* — Your profile\n" +
                       "• */reset* — Start fresh\n" +
                       "• */help* — This menu";
            }

            if (lower is "/status")
            {
                return $"📊 *Your Account*\n\n" +
                       $"👤 Name: {user.DisplayName}\n" +
                       $"📧 Email: {user.Email}\n" +
                       $"📱 Channel: Telegram\n" +
                       $"🕐 Session: {session.CreatedAtUtc:MMM dd, yyyy HH:mm} UTC\n" +
                       $"✈️ State: {session.CurrentState}";
            }

            if (lower is "/reset" or "/restart")
            {
                session.CurrentState = "Verified";
                session.WorkflowStep = null;
                session.ContextData = null;
                await _sessionService.UpdateSessionAsync(session);
                return "🔄 Session reset!\n\nSend any message to start over.";
            }

            if (lower is "/cancel")
            {
                session.CurrentState = "Verified";
                session.WorkflowStep = null;
                await _sessionService.UpdateSessionAsync(session);
                return "❌ Current action cancelled.\n\nType */help* to see what I can do.";
            }

            if (lower is "/agent")
            {
                // Escalate to human
                var sr = await _serviceRequestService.CreateAsync(
                    "HumanEscalation", Channel, user.Id, session.Id,
                    "User requested human agent via Telegram");

                await _serviceRequestService.EscalateAsync(sr.RequestCode, "User requested agent");
                await _auditService.LogAsync("HumanEscalation", "ServiceRequest", sr.RequestCode,
                    user.Id.ToString(), Channel);

                return "🆘 I've flagged your request for a human agent.\n\n" +
                       $"Reference: *{sr.RequestCode}*\n\n" +
                       "A team member will review your conversation and respond.";
            }

            if (lower is "/flight" or "/flights")
            {
                return "✈️ *Flight Search*\n\n" +
                       "I'll help you find flights! Please tell me:\n\n" +
                       "1. Where are you flying *from*? (airport code, e.g., LOS)\n" +
                       "2. Where are you flying *to*? (e.g., LHR)\n" +
                       "3. When? (e.g., 2026-03-15)\n\n" +
                       "Or describe your trip and I'll help!";
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

            // ── Default: AI response ────────────────────────────────────
            try
            {
                // Convert AppSession to UserSession for LLM compatibility
                var legacySession = new UserSession
                {
                    PhoneNumber = user.PhoneNumber ?? $"tg_{user.Id}",
                    Name = user.DisplayName,
                    Email = user.Email,
                    State = UserState.Verified
                };

                var response = await _llm.GetResponseAsync(legacySession, messageText);

                // Create service request for tracking
                await _serviceRequestService.CreateAsync(
                    "AiAssistance", Channel, user.Id, session.Id,
                    $"AI query: {(text.Length > 100 ? text[..100] : text)}");

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI response failed for Telegram user {UserId}", user.Id);
                return "😕 I'm having trouble right now. Try again or use */help* for commands.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // CALLBACK QUERY PROCESSING
        // ═══════════════════════════════════════════════════════════════════
        private async Task ProcessCallbackQueryAsync(JsonElement callback, long updateId)
        {
            var callbackId = callback.TryGetProperty("id", out var cid) ? cid.GetString() : null;
            var data = callback.TryGetProperty("data", out var d) ? d.GetString() : null;

            _logger.LogInformation("Telegram callback query: {Data}", data);

            // Future: handle inline keyboard callbacks
            await Task.CompletedTask;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECURITY
        // ═══════════════════════════════════════════════════════════════════
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
