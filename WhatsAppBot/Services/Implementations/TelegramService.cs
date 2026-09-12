using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Telegram Bot API implementation.
    /// Uses the standard Telegram Bot HTTP API for sending messages and managing webhooks.
    /// </summary>
    public class TelegramService : Interfaces.ITelegramService
    {
        private readonly HttpClient _http;
        private readonly ILogger<TelegramService> _logger;
        private readonly string _botToken;
        private readonly string _baseUrl;

        public TelegramService(HttpClient http, IConfiguration config, ILogger<TelegramService> logger)
        {
            _http = http;
            _logger = logger;
            _botToken = config["Telegram:BotToken"]
                ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
                ?? string.Empty;
            _baseUrl = $"https://api.telegram.org/bot{_botToken}";
        }

        public async Task SendMessageAsync(string chatId, string message, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_botToken))
            {
                _logger.LogWarning("Telegram bot token not configured. Message not sent.");
                return;
            }

            // Telegram has a 4096 character limit per message
            var chunks = SplitMessage(message, 4000);

            foreach (var chunk in chunks)
            {
                var payload = new
                {
                    chat_id = chatId,
                    text = chunk,
                    parse_mode = "Markdown"
                };

                await SendApiRequestAsync("sendMessage", payload, ct);

                if (chunks.Count > 1)
                    await Task.Delay(200, ct);
            }
        }

        public async Task SendPhotoAsync(string chatId, string photoUrl, string? caption = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_botToken))
            {
                _logger.LogWarning("Telegram bot token not configured. Photo not sent.");
                return;
            }

            var payload = new
            {
                chat_id = chatId,
                photo = photoUrl,
                caption = caption ?? string.Empty
            };

            await SendApiRequestAsync("sendPhoto", payload, ct);
        }

        public async Task<bool> SetWebhookAsync(string webhookUrl, string? secretToken = null, CancellationToken ct = default)
        {
            var payload = new
            {
                url = webhookUrl,
                secret_token = secretToken,
                allowed_updates = new[] { "message", "callback_query" }
            };

            var result = await SendApiRequestAsync("setWebhook", payload, ct);
            return result != null;
        }

        public async Task<bool> DeleteWebhookAsync(CancellationToken ct = default)
        {
            var result = await SendApiRequestAsync("deleteWebhook", new { }, ct);
            return result != null;
        }

        public async Task<string?> GetBotInfoAsync(CancellationToken ct = default)
        {
            try
            {
                var response = await _http.GetAsync($"{_baseUrl}/getMe", ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                return body;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get bot info");
                return null;
            }
        }

        private async Task<JsonElement?> SendApiRequestAsync(string method, object payload, CancellationToken ct)
        {
            try
            {
                var url = $"{_baseUrl}/{method}";
                var json = JsonSerializer.Serialize(payload);

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Telegram API {Method} failed: {Status} - {Body}",
                        method, response.StatusCode, body);
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                var ok = doc.RootElement.GetProperty("ok").GetBoolean();

                if (!ok)
                {
                    var desc = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : "Unknown error";
                    _logger.LogError("Telegram API {Method} returned ok=false: {Description}", method, desc);
                    return null;
                }

                _logger.LogDebug("Telegram API {Method} succeeded", method);
                return doc.RootElement;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram API {Method} request failed", method);
                return null;
            }
        }

        private static System.Collections.Generic.List<string> SplitMessage(string message, int maxLength)
        {
            var chunks = new System.Collections.Generic.List<string>();
            message = (message ?? string.Empty).Replace("\r\n", "\n");

            if (message.Length <= maxLength)
            {
                chunks.Add(message.Length == 0 ? " " : message);
                return chunks;
            }

            var lines = message.Split('\n');
            var current = string.Empty;

            foreach (var line in lines)
            {
                var candidate = string.IsNullOrEmpty(current) ? line : $"{current}\n{line}";

                if (candidate.Length <= maxLength)
                {
                    current = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(current))
                {
                    chunks.Add(current);
                    current = string.Empty;
                }

                if (line.Length > maxLength)
                {
                    var remaining = line;
                    while (remaining.Length > maxLength)
                    {
                        chunks.Add(remaining[..maxLength]);
                        remaining = remaining[maxLength..];
                    }
                    if (remaining.Length > 0) current = remaining;
                }
                else
                {
                    current = line;
                }
            }

            if (!string.IsNullOrEmpty(current))
                chunks.Add(current);

            return chunks;
        }
    }
}
