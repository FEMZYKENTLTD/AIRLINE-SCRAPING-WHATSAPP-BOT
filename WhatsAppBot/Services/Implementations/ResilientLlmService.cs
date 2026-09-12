using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Wraps the underlying LLM service with:
    /// - Graceful fallback if AI is unavailable
    /// - Interaction logging (AiInteractionLog) — the AI_REQUEST / AI_FAILURE audit trail
    /// - Error handling that never crashes the application
    /// </summary>
    public class ResilientLlmService : ILLMService
    {
        private readonly ILLMService _inner;
        private readonly ILogger<ResilientLlmService> _logger;
        private readonly AppDbContext _db;

        public ResilientLlmService(
            ILLMService inner,
            ILogger<ResilientLlmService> logger,
            AppDbContext? db = null)
        {
            _inner = inner;
            _logger = logger;
            _db = db;
        }

        public async Task<string> GetResponseAsync(UserSession session, string? userMessage)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var response = await _inner.GetResponseAsync(session, userMessage);
                sw.Stop();

                _logger.LogInformation(
                    "AI response for {Phone} | Latency: {Ms}ms | Length: {Len}",
                    session.PhoneNumber, sw.ElapsedMilliseconds, response.Length);

                await LogInteractionAsync(session, userMessage,
                    success: true, latencyMs: sw.ElapsedMilliseconds,
                    usedFallback: false, errorMessage: null, providerStatusCode: null);

                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "AI service failed for {Phone} after {Ms}ms. Providing fallback.",
                    session.PhoneNumber, sw.ElapsedMilliseconds);

                await LogInteractionAsync(session, userMessage,
                    success: false, latencyMs: sw.ElapsedMilliseconds,
                    usedFallback: true, errorMessage: ex.Message, providerStatusCode: null);

                return GetFallbackResponse(userMessage);
            }
        }

        /// <summary>
        /// Persists an AI interaction log entry. Failures here are logged but
        /// NEVER break the user-facing response path.
        /// </summary>
        private async Task LogInteractionAsync(
            UserSession session, string? userMessage,
            bool success, long latencyMs, bool usedFallback,
            string? errorMessage, int? providerStatusCode)
        {
            if (_db == null) return;

            try
            {
                var entry = new AiInteractionLog
                {
                    Provider = "AzureOpenAI",
                    Model = null,
                    RequestCategory = "chat",
                    Success = success,
                    ProviderStatusCode = providerStatusCode,
                    LatencyMs = latencyMs,
                    UsedFallback = usedFallback,
                    ErrorMessage = errorMessage is { Length: > 990 } ? errorMessage[..990] : errorMessage,
                    Timestamp = DateTime.UtcNow
                };

                // Map the legacy session key to a real user when possible.
                // (UserSession.PhoneNumber carries the channel provider key.)
                var user = await _db.Users
                    .FirstOrDefaultAsync(u => u.PhoneNumber == session.PhoneNumber);
                entry.UserId = user?.Id;

                _db.AiInteractionLogs.Add(entry);
                await _db.SaveChangesAsync();

                _logger.LogDebug("AI interaction logged for {Phone} (success={Success}, fallback={Fallback})",
                    session.PhoneNumber, success, usedFallback);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist AI interaction log (non-critical)");
            }
        }

        /// <summary>
        /// Provides a deterministic fallback when AI is unavailable.
        /// Does not fabricate information: no fake prices, no fake bookings,
        /// no false claims of completed actions.
        /// </summary>
        internal static string GetFallbackResponse(string? userMessage)
        {
            var text = (userMessage ?? "").Trim().ToLowerInvariant();

            if (text.Contains("flight") || text.Contains("book") || text.Contains("fly"))
            {
                return "✈️ I can help you search and book flights!\n\n" +
                       "Type */flight* to start a flight search.\n\n" +
                       "I'll guide you through selecting an airline, route, and dates.";
            }

            if (text.Contains("product") || text.Contains("shop") || text.Contains("catalog"))
            {
                return "🛍️ Browse our products!\n\n" +
                       "Type */products* to see the catalog.\n" +
                       "Use */search <keyword>* to find specific items.";
            }

            if (text.Contains("reservation"))
            {
                return "📋 View your bookings:\n\n" +
                       "Type */mybookings* to see your reservations.\n" +
                       "Type */cancelbooking* to cancel.";
            }

            return "👋 I'm here to help! Here's what I can do:\n\n" +
                   "✈️ */flight* — Search & book flights\n" +
                   "📋 */mybookings* — View reservations\n" +
                   "🛍️ */products* — Browse products\n" +
                   "🆘 */help* — Full command list\n\n" +
                   "⚠️ My AI assistant is temporarily unavailable, but I can still help with the commands above!";
        }
    }
}
