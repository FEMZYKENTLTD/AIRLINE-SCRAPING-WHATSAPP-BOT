using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace WhatsAppBot.Services.CaptchaSolver
{
    /// <summary>
    /// Solves simple image CAPTCHAs using Tesseract OCR.
    /// FREE - runs locally, no API needed.
    /// Works for: simple text CAPTCHAs, distorted text, basic math CAPTCHAs
    /// </summary>
    public class ImageCaptchaSolver
    {
        private readonly ILogger? _logger;
        private readonly string _tessDataPath;

        public ImageCaptchaSolver(ILogger? logger = null)
        {
            _logger = logger;
            _tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

            // Create tessdata directory if needed
            if (!Directory.Exists(_tessDataPath))
            {
                Directory.CreateDirectory(_tessDataPath);
            }
        }

        public async Task EnsureTessDataAsync()
        {
            var engFile = Path.Combine(_tessDataPath, "eng.traineddata");
            if (File.Exists(engFile)) return;

            _logger?.LogInformation("Downloading Tesseract English language data...");

            using var http = new HttpClient();
            var url = "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata";
            var data = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(engFile, data);

            _logger?.LogInformation("Tesseract data downloaded to {Path}", engFile);
        }

        public async Task<string> SolveImageCaptchaAsync(byte[] imageBytes)
        {
            await EnsureTessDataAsync();

            try
            {
                using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);

                // Configure for CAPTCHA text
                engine.SetVariable("tessedit_char_whitelist",
                    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789");
                engine.SetVariable("tessedit_pageseg_mode", "7"); // Single line

                using var img = Pix.LoadFromMemory(imageBytes);

                // Preprocessing: convert to grayscale and threshold
                using var gray = img.ConvertRGBToGray();
                using var binary = gray.BinarizeOtsu();

                using var page = engine.Process(binary);
                var text = page.GetText().Trim();

                _logger?.LogInformation("OCR result: '{Text}' (confidence: {Confidence}%)",
                    text, page.GetMeanConfidence() * 100);

                return text;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "OCR failed");
                return string.Empty;
            }
        }

        public async Task<string> SolveImageCaptchaFromUrlAsync(string imageUrl)
        {
            using var http = new HttpClient();
            var imageBytes = await http.GetByteArrayAsync(imageUrl);
            return await SolveImageCaptchaAsync(imageBytes);
        }

        public string SolveMathCaptcha(string expression)
        {
            // Handle simple math: "3 + 5 = ?", "What is 7 x 2?"
            try
            {
                var cleaned = expression
                    .Replace("What is", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("?", "")
                    .Replace("=", "")
                    .Replace("×", "*")
                    .Replace("x", "*")
                    .Replace("÷", "/")
                    .Trim();

                // Simple eval
                if (cleaned.Contains("+"))
                {
                    var parts = cleaned.Split('+');
                    return (int.Parse(parts[0].Trim()) + int.Parse(parts[1].Trim())).ToString();
                }
                if (cleaned.Contains("-"))
                {
                    var parts = cleaned.Split('-');
                    return (int.Parse(parts[0].Trim()) - int.Parse(parts[1].Trim())).ToString();
                }
                if (cleaned.Contains("*"))
                {
                    var parts = cleaned.Split('*');
                    return (int.Parse(parts[0].Trim()) * int.Parse(parts[1].Trim())).ToString();
                }
                if (cleaned.Contains("/"))
                {
                    var parts = cleaned.Split('/');
                    return (int.Parse(parts[0].Trim()) / int.Parse(parts[1].Trim())).ToString();
                }

                return cleaned;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}