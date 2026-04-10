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

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Azure OpenAI Chat Completions client.
    ///
    /// KEY POINT:
    /// The interface allows userMessage to be null (string?).
    /// This class MUST match that signature to avoid warnings and subtle runtime issues.
    /// </summary>
    public class AzureOpenAiService : ILLMService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<AzureOpenAiService> _logger;
        private readonly int _maxHistoryMessages;

        public AzureOpenAiService(HttpClient http, IConfiguration config, ILogger<AzureOpenAiService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;

            // How many messages of history we keep (pairs of user+assistant)
            _maxHistoryMessages = _config.GetValue<int>("LLM:MaxHistoryMessages", 20);
        }

        /// <summary>
        /// Generates an assistant reply using Azure OpenAI.
        /// userMessage is nullable by contract, so we normalize it safely.
        /// </summary>
        public async Task<string> GetResponseAsync(UserSession session, string? userMessage)
        {
            try
            {
                // Normalize null to empty so we never throw.
                userMessage ??= string.Empty;

                // If empty message slips through, respond politely.
                if (string.IsNullOrWhiteSpace(userMessage))
                    return "Send a message and I’ll reply 😊";

                // Read config
                var endpoint = _config["AzureOpenAI:Endpoint"]?.TrimEnd('/');
                var deployment = _config["AzureOpenAI:Deployment"];
                var apiKey = _config["AzureOpenAI:ApiKey"];
                var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2024-02-15-preview";

                if (string.IsNullOrWhiteSpace(endpoint) ||
                    string.IsNullOrWhiteSpace(deployment) ||
                    string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogError("Azure OpenAI configuration missing. Endpoint/Deployment/ApiKey must be set.");
                    return "⚠️ AI is not configured correctly. Please contact the admin.";
                }

                // Build request URL
                var url = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

                // Build message history payload
                var messages = BuildMessageHistory(session, userMessage);

                // WhatsApp-friendly defaults (still configurable)
                var maxTokens = _config.GetValue<int>("LLM:MaxTokens", 450);
                var temperature = _config.GetValue<double>("LLM:Temperature", 0.7);

                var requestBody = new
                {
                    messages,
                    max_tokens = maxTokens,
                    temperature
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("api-key", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                _logger.LogDebug("Sending request to Azure OpenAI for {Phone}", session.PhoneNumber);

                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Azure OpenAI API error: {StatusCode} - {Error}", response.StatusCode, json);
                    return "😕 I’m having trouble thinking right now. Please try again in a moment.";
                }

                using var doc = JsonDocument.Parse(json);

                var assistantMessage = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "I couldn't generate a response.";

                // Save memory (trimmed)
                session.ConversationHistory.Add(new ChatMessage { Role = "user", Content = userMessage });
                session.ConversationHistory.Add(new ChatMessage { Role = "assistant", Content = assistantMessage });

                // Keep at most N pairs (user+assistant)
                while (session.ConversationHistory.Count > _maxHistoryMessages * 2)
                    session.ConversationHistory.RemoveAt(0);

                _logger.LogInformation("LLM response generated for {Phone}", session.PhoneNumber);

                return assistantMessage.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting LLM response for {Phone}", session.PhoneNumber);
                return "Oops 😅 Something went wrong. Please try again.";
            }
        }

        /// <summary>
        /// Builds a WhatsApp-friendly message payload (system + memory + current user message).
        /// </summary>
        private List<object> BuildMessageHistory(UserSession session, string currentMessage)
        {
            var messages = new List<object>();

            // System prompt: enforce short answers (WhatsApp UX)
            var systemPrompt = $@"
You are a helpful, accurate WhatsApp assistant.

User:
- Name: {session.Name ?? "Unknown"}
- Email: {session.Email ?? "Unknown"}

Hard WhatsApp rules:
1) Keep replies SHORT and mobile-friendly. Aim 4–10 lines max.
2) Prefer bullets. Short paragraphs. No walls of text.
3) If the user asks for code or long content:
   - Provide a short preview + key steps.
   - Ask if they want the full code in parts instead of dumping everything at once.
4) If the user’s request would produce a very long response, summarize and offer to continue.
5) Be friendly but not cheesy. Emojis are okay but minimal.";

            messages.Add(new { role = "system", content = systemPrompt.Trim() });

            foreach (var msg in session.ConversationHistory.TakeLast(_maxHistoryMessages))
                messages.Add(new { role = msg.Role, content = msg.Content });

            messages.Add(new { role = "user", content = currentMessage });

            return messages;
        }
    }
}
