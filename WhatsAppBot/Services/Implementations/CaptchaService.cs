using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Implementations
{
    public class CaptchaService : ICaptchaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _serviceProvider;
        private readonly ILogger<CaptchaService> _logger;
        private const int MAX_RETRY_ATTEMPTS = 3;

        public CaptchaService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<CaptchaService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _apiKey = Environment.GetEnvironmentVariable("CAPTCHA_API_KEY") ??
                      configuration["CaptchaService:ApiKey"] ??
                      string.Empty;
            _serviceProvider = Environment.GetEnvironmentVariable("CAPTCHA_SERVICE_PROVIDER") ??
                               configuration["CaptchaService:Provider"] ??
                               "2captcha";
            _logger = logger;

            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("CAPTCHA API Key not configured. CAPTCHA solving will fail.");
            }
        }

        public async Task<string> SolveRecaptchaV2Async(string siteKey, string pageUrl)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("CAPTCHA API Key not configured");

            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    _logger.LogInformation("Solving reCAPTCHA v2 for {PageUrl} (Attempt {Attempt}/{MaxAttempts})",
                        pageUrl, attempt, MAX_RETRY_ATTEMPTS);

                    if (_serviceProvider.ToLower() == "2captcha")
                    {
                        return await Solve2CaptchaRecaptchaV2(siteKey, pageUrl);
                    }

                    throw new NotSupportedException($"Provider {_serviceProvider} not supported");
                }
                catch (Exception ex) when (attempt < MAX_RETRY_ATTEMPTS)
                {
                    _logger.LogWarning(ex, "CAPTCHA solving attempt {Attempt} failed, retrying...", attempt);
                    await Task.Delay(2000 * attempt);
                }
            }

            throw new Exception($"Failed to solve CAPTCHA after {MAX_RETRY_ATTEMPTS} attempts");
        }

        public async Task<string> SolveRecaptchaV3Async(string siteKey, string pageUrl, string action = "verify")
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("CAPTCHA API Key not configured");

            _logger.LogInformation("Solving reCAPTCHA v3 for {PageUrl}", pageUrl);

            if (_serviceProvider.ToLower() == "2captcha")
            {
                return await Solve2CaptchaRecaptchaV3(siteKey, pageUrl, action);
            }

            throw new NotSupportedException($"Provider {_serviceProvider} not supported for v3");
        }

        public async Task<string> SolveHCaptchaAsync(string siteKey, string pageUrl)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("CAPTCHA API Key not configured");

            _logger.LogInformation("Solving hCaptcha for {PageUrl}", pageUrl);

            if (_serviceProvider.ToLower() == "2captcha")
            {
                var requestUrl = $"https://2captcha.com/in.php?key={_apiKey}&method=hcaptcha&sitekey={siteKey}&pageurl={Uri.EscapeDataString(pageUrl)}&json=1";
                var taskId = await SubmitCaptchaTask(requestUrl);
                return await GetCaptchaResult(taskId);
            }

            throw new NotSupportedException($"Provider {_serviceProvider} not supported");
        }

        public async Task<string> SolveImageCaptchaAsync(byte[] imageBytes)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("CAPTCHA API Key not configured");

            _logger.LogInformation("Solving image CAPTCHA");

            if (_serviceProvider.ToLower() == "2captcha")
            {
                var base64Image = Convert.ToBase64String(imageBytes);
                var requestUrl = $"https://2captcha.com/in.php?key={_apiKey}&method=base64&body={base64Image}&json=1";
                var taskId = await SubmitCaptchaTask(requestUrl);
                return await GetCaptchaResult(taskId);
            }

            throw new NotSupportedException($"Provider {_serviceProvider} not supported");
        }

        private async Task<string> Solve2CaptchaRecaptchaV2(string siteKey, string pageUrl)
        {
            var requestUrl = $"https://2captcha.com/in.php?key={_apiKey}&method=userrecaptcha&googlekey={siteKey}&pageurl={Uri.EscapeDataString(pageUrl)}&json=1";
            var taskId = await SubmitCaptchaTask(requestUrl);
            return await GetCaptchaResult(taskId);
        }

        private async Task<string> Solve2CaptchaRecaptchaV3(string siteKey, string pageUrl, string action)
        {
            var requestUrl = $"https://2captcha.com/in.php?key={_apiKey}&method=userrecaptcha&version=v3&action={action}&min_score=0.3&googlekey={siteKey}&pageurl={Uri.EscapeDataString(pageUrl)}&json=1";
            var taskId = await SubmitCaptchaTask(requestUrl);
            return await GetCaptchaResult(taskId);
        }

        private async Task<string> SubmitCaptchaTask(string requestUrl)
        {
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonDocument>(content);

            if (result?.RootElement.TryGetProperty("status", out var status) == true && status.GetInt32() == 1)
            {
                return result.RootElement.GetProperty("request").GetString() ??
                       throw new Exception("Task ID is null");
            }

            var errorMsg = result?.RootElement.GetProperty("request").GetString() ?? "Unknown error";
            throw new Exception($"CAPTCHA submission failed: {errorMsg}");
        }

        private async Task<string> GetCaptchaResult(string taskId)
        {
            var maxAttempts = 60;
            var delay = 5000; // 5 seconds

            for (int i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(delay);

                var resultUrl = $"https://2captcha.com/res.php?key={_apiKey}&action=get&id={taskId}&json=1";
                var response = await _httpClient.GetAsync(resultUrl);
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonDocument>(content);

                if (result?.RootElement.TryGetProperty("status", out var status) == true && status.GetInt32() == 1)
                {
                    return result.RootElement.GetProperty("request").GetString() ??
                           throw new Exception("CAPTCHA solution is null");
                }

                var request = result?.RootElement.GetProperty("request").GetString();
                if (request != "CAPCHA_NOT_READY")
                {
                    throw new Exception($"CAPTCHA solving failed: {request}");
                }

                _logger.LogDebug("CAPTCHA not ready yet, attempt {Attempt}/{MaxAttempts}", i + 1, maxAttempts);
            }

            throw new TimeoutException("CAPTCHA solving timed out after 5 minutes");
        }
    }
}