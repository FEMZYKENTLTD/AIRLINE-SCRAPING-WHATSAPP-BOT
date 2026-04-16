using System;
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
    /// IMAGE GENERATION SERVICE
    /// ═══════════════════════════════════════════════════════════════
    /// Uses Azure OpenAI to generate images from text prompts.
    ///
    /// Primary model: gpt-image-1 (newest, best quality)
    /// Fallback model: dall-e-2 (if gpt-image-1 unavailable)
    ///
    /// Triggered when users type:
    /// - /image <prompt>
    /// - "generate image of..."
    /// - "draw me a..."
    /// - "create a picture of..."
    /// ═══════════════════════════════════════════════════════════════
    /// </summary>
    public class ImageGenerationService : IImageGenerationService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<ImageGenerationService> _logger;

        public ImageGenerationService(
            HttpClient http,
            IConfiguration config,
            ILogger<ImageGenerationService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        public async Task<ImageGenerationResult> GenerateImageAsync(
            string prompt,
            string size = "1024x1024",
            CancellationToken ct = default)
        {
            var endpoint = _config["AzureOpenAI:Endpoint"]?.TrimEnd('/');
            var apiKey = _config["AzureOpenAI:ApiKey"];
            var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2024-02-15-preview";

            // Try gpt-image-1 first, fall back to dall-e-2
            var deployment = Environment.GetEnvironmentVariable("AZURE_IMAGE_DEPLOYMENT")
                             ?? _config["AzureOpenAI:ImageDeployment"]
                             ?? "gpt-image-1";

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                return new ImageGenerationResult
                {
                    Success = false,
                    ErrorMessage = "Image generation not configured"
                };
            }

            try
            {
                _logger.LogInformation(
                    "Generating image with {Model}: {Prompt}",
                    deployment, prompt.Length > 50 ? prompt[..50] + "..." : prompt);

                // Azure OpenAI image generation endpoint
                var url = $"{endpoint}/openai/deployments/{deployment}/images/generations" +
                          $"?api-version={apiVersion}";

                var requestBody = new
                {
                    prompt = prompt,
                    n = 1,
                    size = size,
                    response_format = "url"
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
                    _logger.LogWarning(
                        "Image generation failed with {Model}: {Status} — {Body}",
                        deployment, response.StatusCode, json);

                    // Try dall-e-2 as fallback
                    if (deployment != "dall-e-2")
                    {
                        _logger.LogInformation("Trying dall-e-2 as fallback...");
                        return await GenerateWithFallbackAsync(
                            prompt, size, endpoint, apiKey, apiVersion, ct);
                    }

                    return new ImageGenerationResult
                    {
                        Success = false,
                        ErrorMessage = $"Generation failed: {response.StatusCode}"
                    };
                }

                using var doc = JsonDocument.Parse(json);
                var imageUrl = doc.RootElement
                    .GetProperty("data")[0]
                    .GetProperty("url")
                    .GetString();

                _logger.LogInformation("✅ Image generated successfully with {Model}", deployment);

                return new ImageGenerationResult
                {
                    Success = true,
                    ImageUrl = imageUrl,
                    ModelUsed = deployment
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image generation error");
                return new ImageGenerationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Fallback to dall-e-2 if primary model fails.
        /// </summary>
        private async Task<ImageGenerationResult> GenerateWithFallbackAsync(
            string prompt, string size, string endpoint,
            string apiKey, string apiVersion, CancellationToken ct)
        {
            try
            {
                var url = $"{endpoint}/openai/deployments/dall-e-2/images/generations" +
                          $"?api-version={apiVersion}";

                var requestBody = new { prompt, n = 1, size };

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("api-key", apiKey);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request, ct);
                var json = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                    return new ImageGenerationResult
                    {
                        Success = false,
                        ErrorMessage = "All image models failed"
                    };

                using var doc = JsonDocument.Parse(json);
                var imageUrl = doc.RootElement
                    .GetProperty("data")[0]
                    .GetProperty("url")
                    .GetString();

                return new ImageGenerationResult
                {
                    Success = true,
                    ImageUrl = imageUrl,
                    ModelUsed = "dall-e-2"
                };
            }
            catch (Exception ex)
            {
                return new ImageGenerationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}