using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// Meta WhatsApp Cloud API sender.
    ///
    /// Supports:
    /// - Text messages (chunked for safety)
    /// - Image messages (public URL required)
    ///
    /// NOTE:
    /// Meta Cloud API does NOT accept "whatsapp:" prefix. Use raw E.164 digits like 234xxxxxxxxxx
    /// </summary>
    public class MetaWhatsAppService(HttpClient http, IConfiguration config, ILogger<MetaWhatsAppService> logger) : IWhatsAppService
    {
        private readonly HttpClient _http = http;
        private readonly ILogger<MetaWhatsAppService> _logger = logger;

        private readonly string _accessToken = config["MetaWhatsApp:AccessToken"]
            ?? throw new InvalidOperationException("MetaWhatsApp:AccessToken missing");

        private readonly string _phoneNumberId = config["MetaWhatsApp:PhoneNumberId"]
            ?? throw new InvalidOperationException("MetaWhatsApp:PhoneNumberId missing");

        private readonly string _graphBase = (config["MetaWhatsApp:GraphBase"] ?? "https://graph.facebook.com").TrimEnd('/');
        private readonly string _apiVersion = (config["MetaWhatsApp:ApiVersion"] ?? "v20.0").Trim();

        // Conservative chunk size for WhatsApp readability (and reduces edge failures).
        private const int MaxChunk = 1500;

        public async Task SendMessageAsync(string to, string message)
        {
            if (string.IsNullOrWhiteSpace(to))
                return;

            message ??= string.Empty;

            // Ensure authorization is always present
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var toNumber = StripWhatsAppPrefix(to);
            var chunks = SplitMessageSmart(message, MaxChunk);

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];

                // Add part labels when message is split (better UX)
                if (chunks.Count > 1)
                    chunk = $"({i + 1}/{chunks.Count})\n{chunk}";

                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = toNumber,
                    type = "text",
                    text = new
                    {
                        body = chunk,
                        preview_url = false
                    }
                };

                await SendPayloadAsync(payload, toNumber);

                if (chunks.Count > 1)
                    await Task.Delay(150);
            }
        }

        public async Task SendImageAsync(string to, string imageUrl, string? caption = null)
        {
            if (string.IsNullOrWhiteSpace(to))
                return;

            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var toNumber = StripWhatsAppPrefix(to);

            // NOTE:
            // "link" must be a public HTTPS URL reachable by Meta servers.
            var payload = new
            {
                messaging_product = "whatsapp",
                to = toNumber,
                type = "image",
                image = new
                {
                    link = imageUrl,
                    caption = caption ?? string.Empty
                }
            };

            await SendPayloadAsync(payload, toNumber);
        }

        private async Task SendPayloadAsync(object payload, string toNumber)
        {
            var url = $"{_graphBase}/{_apiVersion}/{_phoneNumberId}/messages";

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            var res = await _http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogError("Meta send failed to {To}: {Status} {Body}", toNumber, res.StatusCode, body);
                throw new InvalidOperationException($"Meta send failed: {res.StatusCode}");
            }

            _logger.LogInformation("Meta message sent to {To}. Response: {Body}", toNumber, body);
        }

        private static string StripWhatsAppPrefix(string number)
        {
            if (string.IsNullOrWhiteSpace(number))
                return string.Empty;

            return number.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
                ? number["whatsapp:".Length..].Trim()
                : number.Trim();
        }

        /// <summary>
        /// Splits long messages safely.
        /// Prefers newline splits to preserve formatting; falls back to space splits.
        /// </summary>
        private static List<string> SplitMessageSmart(string message, int maxLength)
        {
            var chunks = new List<string>();

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

                // Flush current chunk
                if (!string.IsNullOrEmpty(current))
                {
                    chunks.Add(current);
                    current = string.Empty;
                }

                // If single line too long, split it further
                if (line.Length > maxLength)
                    chunks.AddRange(SplitLongLine(line, maxLength));
                else
                    current = line;
            }

            if (!string.IsNullOrEmpty(current))
                chunks.Add(current);

            return chunks;
        }

        /// <summary>
        /// Splits a long single-line string into parts.
        /// Returns List for better performance (removes analyzer warning).
        /// </summary>
        private static List<string> SplitLongLine(string line, int maxLength)
        {
            var parts = new List<string>();
            var remaining = line;

            while (remaining.Length > maxLength)
            {
                // Try breaking on space
                var breakPoint = remaining.LastIndexOf(' ', maxLength);
                if (breakPoint < maxLength / 2)
                    breakPoint = maxLength; // hard cut

                parts.Add(remaining[..breakPoint].Trim());
                remaining = remaining[breakPoint..].Trim();
            }

            if (remaining.Length > 0)
                parts.Add(remaining);

            return parts;
        }
    }
}
