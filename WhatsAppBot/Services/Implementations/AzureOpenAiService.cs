using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Learning;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Azure OpenAI Chat Completions client.
    /// Enhanced with:
    /// - RAG (Retrieval Augmented Generation) from database
    /// - User preference learning
    /// - Conversation analysis
    /// - Full bot personality
    /// Compatible with Azure OpenAI and Azure AI Foundry endpoints.
    /// </summary>
    public class AzureOpenAiService : ILLMService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<AzureOpenAiService> _logger;
        private readonly IKnowledgeService? _knowledge;
        private readonly int _maxHistoryMessages;

        // Constructor without knowledge (backwards compatible)
        public AzureOpenAiService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<AzureOpenAiService> logger,
            IKnowledgeService? knowledge = null)
        {
            _http = httpClientFactory.CreateClient();
            _config = config;
            _logger = logger;
            _knowledge = knowledge;
            _maxHistoryMessages = _config.GetValue<int>("LLM:MaxHistoryMessages", 20);
        }

        /// <summary>
        /// Generates an assistant reply using Azure OpenAI.
        /// Now enhanced with RAG: searches database for context before calling AI.
        /// </summary>
        public async Task<string> GetResponseAsync(UserSession session, string? userMessage)
        {
            try
            {
                userMessage ??= string.Empty;

                if (string.IsNullOrWhiteSpace(userMessage))
                    return "Send a message and I'll reply 😊";

                // ── Read config ──────────────────────────────────────────
                var endpoint = _config["AzureOpenAI:Endpoint"]?.TrimEnd('/');
                var deployment = _config["AzureOpenAI:Deployment"];
                var apiKey = _config["AzureOpenAI:ApiKey"];
                var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2024-02-15-preview";

                if (string.IsNullOrWhiteSpace(endpoint) ||
                    string.IsNullOrWhiteSpace(deployment) ||
                    string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogError("Azure OpenAI config missing: Endpoint/Deployment/ApiKey required.");
                    return "⚠️ AI is not configured correctly. Please contact the admin.";
                }

                // ── RAG: Search knowledge base for relevant context ──────
                string? knowledgeContext = null;
                string? preferredAirline = null;
                string? commonOrigin = null;

                if (_knowledge != null)
                {
                    try
                    {
                        knowledgeContext = await _knowledge.FindAnswerAsync(userMessage);

                        var airlinePref = await _knowledge.GetPreferenceAsync(
                            session.PhoneNumber, "preferred_airline");
                        preferredAirline = airlinePref?.PreferenceValue;

                        var originPref = await _knowledge.GetPreferenceAsync(
                            session.PhoneNumber, "common_origin");
                        commonOrigin = originPref?.PreferenceValue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Knowledge lookup failed, continuing without RAG");
                    }
                }

                // ── Build API URL ────────────────────────────────────────
                var url = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

                // ── Build messages with full context ─────────────────────
                var messages = BuildMessageHistory(
                    session, userMessage, knowledgeContext, preferredAirline, commonOrigin);

                var maxTokens = _config.GetValue<int>("LLM:MaxTokens", 450);
                var temperature = _config.GetValue<double>("LLM:Temperature", 0.7);

                var requestBody = new { messages, max_tokens = maxTokens, temperature };

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("api-key", apiKey);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                _logger.LogDebug("Azure OpenAI request for {Phone}", session.PhoneNumber);

                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Azure OpenAI error: {Status} - {Body}", response.StatusCode, json);
                    return "😕 I'm having trouble thinking right now. Please try again in a moment.";
                }

                using var doc = JsonDocument.Parse(json);
                var assistantMessage = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "I couldn't generate a response.";

                // ── Save conversation memory ─────────────────────────────
                session.ConversationHistory.Add(new ChatMessage { Role = "user", Content = userMessage });
                session.ConversationHistory.Add(new ChatMessage { Role = "assistant", Content = assistantMessage });

                while (session.ConversationHistory.Count > _maxHistoryMessages * 2)
                    session.ConversationHistory.RemoveAt(0);

                // ── Background learning from conversation ────────────────
                if (_knowledge != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _knowledge.LearnFromConversationAsync(
                                session.PhoneNumber, userMessage, assistantMessage, default);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Background learning failed (non-critical)");
                        }
                    });
                }

                _logger.LogInformation("LLM response for {Phone} | RAG: {HasKnowledge} | Tokens: ~{Tokens}",
                    session.PhoneNumber,
                    knowledgeContext != null ? "YES" : "NO",
                    assistantMessage.Length / 4);

                return assistantMessage.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM error for {Phone}", session.PhoneNumber);
                return "Oops 😅 Something went wrong. Please try again.";
            }
        }

        /// <summary>
        /// Builds the full message payload:
        /// System prompt (personality + rules + knowledge) + History + Current message
        /// </summary>
        private List<object> BuildMessageHistory(
            UserSession session,
            string currentMessage,
            string? knowledgeContext,
            string? preferredAirline,
            string? commonOrigin)
        {
            var messages = new List<object>();

            var botName = _config["Bot:Name"] ?? "Femzyk_Aje_Bot";
            var company = _config["Bot:Company"] ?? "FEMZYK ENTERPRISES LTD";
            var supportEmail = _config["Bot:SupportEmail"] ?? "femzykenterprisesltd@gmail.com";

            var systemPrompt = $@"
You are *{botName}*, an advanced AI Travel & E-commerce Assistant for *{company}*.
You are powered by Azure OpenAI and have access to live airline data, flight prices, and product catalogs.

═══════════════════════════════════════
USER CONTEXT (Use this to personalize responses):
═══════════════════════════════════════
- Name: {session.Name ?? "Unknown"}
- Email: {session.Email ?? "Unknown"}
- Preferred Airline: {preferredAirline ?? "Not detected yet"}
- Frequent Origin Airport: {commonOrigin ?? "Not detected yet"}
- Messages in this session: {session.ConversationHistory.Count}

═══════════════════════════════════════
AVAILABLE COMMANDS (Suggest these when relevant):
═══════════════════════════════════════
✈️ Flight Commands:
  /flight       → Search & book flights (all airlines)
  /flightcancel → Cancel current flight search
  /mybookings   → View all reservations
  /cancelbooking → Cancel a reservation

🛍️ Catalog Commands:
  /products          → Browse latest products
  /search <keyword>  → Search products by keyword
  /product <SKU>     → View product details

⚙️ Account Commands:
  /status → View profile & session info
  /clear  → Clear conversation memory
  /reset  → Start a completely fresh session
  /help   → Show full command list

═══════════════════════════════════════
SUPPORTED AIRLINES:
═══════════════════════════════════════
1. 🇳🇬 Arik Air        - Domestic & regional Nigerian flights
2. 🇳🇬 Air Peace       - Nigerian airline, domestic & international  
3. 🇹🇷 Turkish Airlines - International, hub in Istanbul
4. 🇩🇪 Lufthansa       - International, hub in Frankfurt

═══════════════════════════════════════
LIVE DATABASE KNOWLEDGE (Use this if relevant):
═══════════════════════════════════════
{(string.IsNullOrWhiteSpace(knowledgeContext)
    ? "No specific database matches found. Answer from general knowledge."
    : knowledgeContext)}

═══════════════════════════════════════
STRICT WHATSAPP RULES (NEVER break these):
═══════════════════════════════════════
1) Keep replies SHORT and mobile-friendly. MAX 10 lines.
2) Use *bold* for important info. Use bullet points.
3) NEVER dump long walls of text. Summarize and offer to continue.
4) If user asks about flights → suggest /flight command.
5) If user asks about products → suggest /products command.
6) If user asks about their bookings → suggest /mybookings.
7) If user wants to cancel → suggest /cancelbooking.
8) Be warm, professional, and helpful. Use emojis sparingly.
9) NEVER make up flight prices. Only share data from the database above.
10) For technical support → {supportEmail}
11) Always suggest a clear next action at the end of your reply.
12) If user greets you → greet back AND show 2-3 things they can do.";

            messages.Add(new { role = "system", content = systemPrompt.Trim() });

            // Add conversation history (last N messages)
            foreach (var msg in session.ConversationHistory.TakeLast(_maxHistoryMessages))
                messages.Add(new { role = msg.Role, content = msg.Content });

            // Add current user message
            messages.Add(new { role = "user", content = currentMessage });

            return messages;
        }
    }
}