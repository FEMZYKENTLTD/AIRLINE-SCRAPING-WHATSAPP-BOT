using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WhatsAppBot.Services.Media
{
    /// <summary>
    /// ═══════════════════════════════════════════════════════════════
    /// VISION SERVICE — IMAGE UNDERSTANDING
    /// ═══════════════════════════════════════════════════════════════
    /// Uses gpt-4o (vision model) to understand images.
    ///
    /// What it can do:
    /// - Read text in images (OCR)
    /// - Describe images
    /// - Answer questions about images
    /// - Extract flight details from screenshots
    /// - Read passport/ID information
    /// - Analyze travel documents
    ///
    /// Model: gpt-4o (deployment: "gpt-4o")
    /// ═══════════════════════════════════════════════════════════════
    /// </summary>
    public class VisionService : IVisionService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<VisionService> _logger;

        public VisionService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<VisionService> logger)
        {
            _http = httpClientFactory.CreateClient();
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Analyzes an image from a public URL.
        /// Sends URL directly to GPT-4o Vision API.
        /// </summary>
        public async Task<string?> AnalyzeImageFromUrlAsync(
            string imageUrl,
            string? prompt = null,
            CancellationToken ct = default)
        {
            prompt ??= "Please describe this image in detail. " +
                       "If it contains text, extract all text. " +
                       "If it contains flight or travel information, extract all details.";

            try
            {
                _logger.LogInformation("Analyzing image from URL: {Url}", imageUrl);

                // Build message with image URL
                var messages = new List<object>
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = imageUrl } }
                        }
                    }
                };

                return await CallVisionApiAsync(messages, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Vision analysis error for URL: {Url}", imageUrl);
                return null;
            }
        }

        /// <summary>
        /// Analyzes an image from raw bytes.
        /// Converts to base64 and sends to GPT-4o Vision API.
        /// </summary>
        public async Task<string?> AnalyzeImageAsync(
            byte[] imageBytes,
            string? prompt = null,
            CancellationToken ct = default)
        {
            prompt ??= "Please describe this image in detail. " +
                       "Extract any text, numbers, or important information visible.";

            try
            {
                _logger.LogInformation(
                    "Analyzing image from bytes ({Size} bytes)", imageBytes.Length);

                // Convert to base64 data URL
                var base64 = Convert.ToBase64String(imageBytes);
                var dataUrl = $"data:image/jpeg;base64,{base64}";

                var messages = new List<object>
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = dataUrl } }
                        }
                    }
                };

                return await CallVisionApiAsync(messages, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Vision analysis error from bytes");
                return null;
            }
        }

        /// <summary>
        /// Internal: Call the GPT-4o Vision API.
        /// </summary>
        private async Task<string?> CallVisionApiAsync(
            List<object> messages, CancellationToken ct)
        {
            var endpoint = _config["AzureOpenAI:Endpoint"]?.TrimEnd('/');
            var apiKey = _config["AzureOpenAI:ApiKey"];
            var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2024-02-15-preview";

            // Vision requires gpt-4o (not gpt-4o-mini)
            var visionDeployment = Environment.GetEnvironmentVariable("AZURE_VISION_DEPLOYMENT")
                                   ?? _config["AzureOpenAI:VisionDeployment"]
                                   ?? "gpt-4o";

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Vision service not configured");
                return null;
            }

            var url = $"{endpoint}/openai/deployments/{visionDeployment}/chat/completions" +
                      $"?api-version={apiVersion}";

            var requestBody = new
            {
                messages,
                max_tokens = 1000
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("api-key", apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Vision API error: {Status} — {Body}",
                    response.StatusCode, json);
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var result = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            _logger.LogInformation("✅ Vision analysis complete");
            return result?.Trim();
        }
    }
}