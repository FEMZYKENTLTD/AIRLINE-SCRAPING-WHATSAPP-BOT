using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Wraps the underlying LLM service with:
    /// - Graceful fallback if AI is unavailable
    /// - Interaction logging
    /// - Error handling that never crashes the application
    /// </summary>
    public class ResilientLlmService : ILLMService
    {
        private readonly ILLMService _inner;
        private readonly ILogger<ResilientLlmService> _logger;

        public ResilientLlmService(ILLMService inner, ILogger<ResilientLlmService> logger)
        {
            _inner = inner;
            _logger = logger;
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

                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "AI service failed for {Phone} after {Ms}ms. Providing fallback.",
                    session.PhoneNumber, sw.ElapsedMilliseconds);

                return GetFallbackResponse(userMessage);
            }
        }

        /// <summary>
        /// Provides a deterministic fallback when AI is unavailable.
        /// Does not fabricate information.
        /// </summary>
        private static string GetFallbackResponse(string? userMessage)
        {
            var text = (userMessage ?? "").Trim().ToLowerInvariant();

            if (text.Contains("flight") || text.Contains("book") || text.Contains("fly"))
            {
                return "✈️ I can help you search and book flights!\n\nType */flight* to start a flight search.\n\nI'll guide you through selecting an airline, route, and dates.";
            }

            if (text.Contains("product") || text.Contains("shop") || text.Contains("catalog"))
            {
                return "🛍️ Browse our products!\n\nType */products* to see the catalog.\nUse */search <keyword>* to find specific items.";
            }

            if (text.Contains("book") || text.Contains("reservation"))
            {
                return "📋 View your bookings:\n\nType */mybookings* to see your reservations.\nType */cancelbooking* to cancel.";
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
