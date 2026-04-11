using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace WhatsAppBot.Services.CaptchaSolver
{
    /// <summary>
    /// Solves reCAPTCHA v2 by:
    /// 1. Clicking the checkbox
    /// 2. If image challenge appears, switching to audio challenge
    /// 3. Downloading the audio file
    /// 4. Converting speech to text using Azure FREE tier or local whisper
    /// 
    /// FREE - Azure Speech has 5 hours/month free tier
    /// </summary>
    public class RecaptchaAudioSolver
    {
        private readonly ILogger? _logger;
        private readonly string? _azureSpeechKey;
        private readonly string? _azureSpeechRegion;

        public RecaptchaAudioSolver(
            ILogger? logger = null,
            string? azureSpeechKey = null,
            string? azureSpeechRegion = null)
        {
            _logger = logger;
            _azureSpeechKey = azureSpeechKey ??
                              Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
            _azureSpeechRegion = azureSpeechRegion ??
                                 Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                                 "eastus";
        }

        public async Task<bool> SolveRecaptchaOnPageAsync(IPage page)
        {
            try
            {
                _logger?.LogInformation("Attempting to solve reCAPTCHA...");

                // Step 1: Find and click the reCAPTCHA checkbox
                var recaptchaFrame = await FindRecaptchaFrame(page);
                if (recaptchaFrame == null)
                {
                    _logger?.LogWarning("reCAPTCHA iframe not found");
                    return false;
                }

                // Click checkbox
                var checkbox = await recaptchaFrame.QuerySelectorAsync("#recaptcha-anchor");
                if (checkbox != null)
                {
                    await checkbox.ClickAsync();
                    _logger?.LogInformation("Clicked reCAPTCHA checkbox");
                    await Task.Delay(3000);
                }

                // Check if solved (sometimes checkbox alone works with stealth browser)
                if (await IsRecaptchaSolved(recaptchaFrame))
                {
                    _logger?.LogInformation("reCAPTCHA solved with checkbox click alone!");
                    return true;
                }

                // Step 2: Challenge appeared, try audio method
                var challengeFrame = await FindChallengeFrame(page);
                if (challengeFrame == null)
                {
                    _logger?.LogWarning("reCAPTCHA challenge frame not found");
                    return false;
                }

                // Click audio button
                var audioButton = await challengeFrame.QuerySelectorAsync("#recaptcha-audio-button");
                if (audioButton != null)
                {
                    await audioButton.ClickAsync();
                    _logger?.LogInformation("Switched to audio challenge");
                    await Task.Delay(2000);
                }

                // Step 3: Get audio URL
                var audioSrc = await challengeFrame.EvalOnSelectorAsync<string>(
                    "#audio-source", "el => el.src");

                if (string.IsNullOrEmpty(audioSrc))
                {
                    _logger?.LogWarning("Could not find audio source");
                    return false;
                }

                // Step 4: Download and transcribe audio
                var transcription = await TranscribeAudioAsync(audioSrc);

                if (string.IsNullOrEmpty(transcription))
                {
                    _logger?.LogWarning("Audio transcription failed");
                    return false;
                }

                _logger?.LogInformation("Audio transcription: '{Text}'", transcription);

                // Step 5: Enter the answer
                var responseInput = await challengeFrame.QuerySelectorAsync("#audio-response");
                if (responseInput != null)
                {
                    await responseInput.FillAsync(transcription);
                    await Task.Delay(500);

                    // Click verify
                    var verifyButton = await challengeFrame.QuerySelectorAsync("#recaptcha-verify-button");
                    if (verifyButton != null)
                    {
                        await verifyButton.ClickAsync();
                        await Task.Delay(3000);
                    }
                }

                // Check if solved
                var solved = await IsRecaptchaSolved(recaptchaFrame);
                _logger?.LogInformation("reCAPTCHA solved: {Solved}", solved);

                return solved;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to solve reCAPTCHA");
                return false;
            }
        }

        private async Task<IFrame?> FindRecaptchaFrame(IPage page)
        {
            foreach (var frame in page.Frames)
            {
                if (frame.Url.Contains("recaptcha/api2/anchor") ||
                    frame.Url.Contains("recaptcha/enterprise/anchor"))
                {
                    return frame;
                }
            }

            // Try finding by iframe selector
            var iframe = await page.QuerySelectorAsync("iframe[src*='recaptcha']");
            if (iframe != null)
            {
                return await iframe.ContentFrameAsync();
            }

            return null;
        }

        private async Task<IFrame?> FindChallengeFrame(IPage page)
        {
            foreach (var frame in page.Frames)
            {
                if (frame.Url.Contains("recaptcha/api2/bframe") ||
                    frame.Url.Contains("recaptcha/enterprise/bframe"))
                {
                    return frame;
                }
            }

            return null;
        }

        private async Task<bool> IsRecaptchaSolved(IFrame recaptchaFrame)
        {
            try
            {
                var anchor = await recaptchaFrame.QuerySelectorAsync("#recaptcha-anchor");
                if (anchor != null)
                {
                    var ariaChecked = await anchor.GetAttributeAsync("aria-checked");
                    return ariaChecked == "true";
                }
            }
            catch { }

            return false;
        }

        private async Task<string> TranscribeAudioAsync(string audioUrl)
        {
            // Download audio file
            using var http = new HttpClient();
            var audioBytes = await http.GetByteArrayAsync(audioUrl);

            // Save temp file
            var tempFile = Path.Combine(Path.GetTempPath(), $"captcha_audio_{Guid.NewGuid()}.mp3");
            await File.WriteAllBytesAsync(tempFile, audioBytes);

            try
            {
                // Try Azure Speech (FREE tier: 5 hours/month)
                if (!string.IsNullOrEmpty(_azureSpeechKey))
                {
                    return await TranscribeWithAzureSpeech(tempFile);
                }

                // Fallback: Use the bot's existing Azure OpenAI for transcription
                _logger?.LogWarning("No speech service configured. " +
                    "Set AZURE_SPEECH_KEY for FREE audio transcription (5 hrs/month).");
                return string.Empty;
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        private async Task<string> TranscribeWithAzureSpeech(string audioFilePath)
        {
            try
            {
                var config = Microsoft.CognitiveServices.Speech.SpeechConfig.FromSubscription(
                    _azureSpeechKey!, _azureSpeechRegion!);
                config.SpeechRecognitionLanguage = "en-US";

                using var audioConfig = Microsoft.CognitiveServices.Speech.Audio.AudioConfig.FromWavFileInput(audioFilePath);
                using var recognizer = new Microsoft.CognitiveServices.Speech.SpeechRecognizer(config, audioConfig);

                var result = await recognizer.RecognizeOnceAsync();

                if (result.Reason == Microsoft.CognitiveServices.Speech.ResultReason.RecognizedSpeech)
                {
                    _logger?.LogInformation("Speech recognized: '{Text}'", result.Text);
                    return result.Text.Trim();
                }

                _logger?.LogWarning("Speech recognition failed: {Reason}", result.Reason);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Azure Speech transcription failed");
                return string.Empty;
            }
        }
    }
}