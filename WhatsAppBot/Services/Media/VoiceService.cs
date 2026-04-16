using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WhatsAppBot.Services.Media
{
    /// <summary>
    /// ═══════════════════════════════════════════════════════════════
    /// VOICE SERVICE
    /// ═══════════════════════════════════════════════════════════════
    /// Uses Azure Speech Service for audio features.
    ///
    /// Text to Speech (TTS):
    ///   - Bot can send voice messages via WhatsApp
    ///   - Uses neural voices (sounds very natural)
    ///   - Useful for: confirmations, announcements
    ///
    /// Speech to Text (STT):
    ///   - Process voice notes users send
    ///   - Solve audio CAPTCHAs during scraping
    ///   - FREE tier: 5 hours per month
    ///
    /// Resource: aje-speech
    /// Endpoint: eastus.stt.speech.microsoft.com
    /// ═══════════════════════════════════════════════════════════════
    /// </summary>
    public class VoiceService : IVoiceService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<VoiceService> _logger;

        // Default voice for Text-to-Speech
        // Options: en-US-JennyNeural, en-GB-SoniaNeural, en-NG-AbeoNeural
        private const string DefaultVoice = "en-US-JennyNeural";

        public VoiceService(
            HttpClient http,
            IConfiguration config,
            ILogger<VoiceService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Converts text to speech and saves as .wav file.
        /// Returns the file path to the generated audio.
        /// </summary>
        public async Task<string?> TextToSpeechAsync(
            string text, CancellationToken ct = default)
        {
            var speechKey = _config["AzureSpeech:Key"] ??
                            Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
            var speechRegion = _config["AzureSpeech:Region"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                               "eastus";

            if (string.IsNullOrWhiteSpace(speechKey))
            {
                _logger.LogWarning("Speech key not configured — TTS unavailable");
                return null;
            }

            try
            {
                _logger.LogInformation("Converting text to speech ({Length} chars)", text.Length);

                // Configure Azure Speech
                var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
                speechConfig.SpeechSynthesisVoiceName = DefaultVoice;

                // Output to temp file
                var outputPath = Path.Combine(
                    Path.GetTempPath(),
                    $"tts_{Guid.NewGuid()}.wav");

                using var audioConfig = AudioConfig.FromWavFileOutput(outputPath);
                using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig);

                var result = await synthesizer.SpeakTextAsync(text);

                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    _logger.LogInformation("✅ TTS complete: {Path}", outputPath);
                    return outputPath;
                }

                _logger.LogWarning("TTS failed: {Reason}", result.Reason);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Text-to-speech error");
                return null;
            }
        }

        /// <summary>
        /// Converts speech audio bytes to text.
        /// Used for: audio CAPTCHA solving, user voice messages.
        /// </summary>
        public async Task<string?> SpeechToTextAsync(
            byte[] audioBytes, CancellationToken ct = default)
        {
            var speechKey = _config["AzureSpeech:Key"] ??
                            Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
            var speechRegion = _config["AzureSpeech:Region"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                               "eastus";

            if (string.IsNullOrWhiteSpace(speechKey))
            {
                _logger.LogWarning("Speech key not configured — STT unavailable");
                return null;
            }

            try
            {
                // Save bytes to temp file
                var tempFile = Path.Combine(
                    Path.GetTempPath(),
                    $"stt_{Guid.NewGuid()}.wav");

                await File.WriteAllBytesAsync(tempFile, audioBytes, ct);

                try
                {
                    return await TranscribeFileAsync(tempFile, speechKey, speechRegion);
                }
                finally
                {
                    // Cleanup temp file
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Speech-to-text error");
                return null;
            }
        }

        /// <summary>
        /// Downloads audio from URL and converts to text.
        /// Used for: reCAPTCHA audio challenges.
        /// </summary>
        public async Task<string?> SpeechToTextFromUrlAsync(
            string audioUrl, CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("Downloading audio from {Url}", audioUrl);

                var audioBytes = await _http.GetByteArrayAsync(audioUrl, ct);
                return await SpeechToTextAsync(audioBytes, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "STT from URL error: {Url}", audioUrl);
                return null;
            }
        }

        /// <summary>
        /// Internal: Transcribe an audio file using Azure Speech SDK.
        /// </summary>
        private async Task<string?> TranscribeFileAsync(
            string filePath, string speechKey, string speechRegion)
        {
            var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
            speechConfig.SpeechRecognitionLanguage = "en-US";

            using var audioConfig = AudioConfig.FromWavFileInput(filePath);
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            var result = await recognizer.RecognizeOnceAsync();

            switch (result.Reason)
            {
                case ResultReason.RecognizedSpeech:
                    _logger.LogInformation("✅ STT result: '{Text}'", result.Text);
                    return result.Text.Trim();

                case ResultReason.NoMatch:
                    _logger.LogWarning("STT: No speech recognized");
                    return null;

                case ResultReason.Canceled:
                    var cancellation = CancellationDetails.FromResult(result);
                    _logger.LogWarning("STT canceled: {Reason} — {Details}",
                        cancellation.Reason, cancellation.ErrorDetails);
                    return null;

                default:
                    _logger.LogWarning("STT unexpected result: {Reason}", result.Reason);
                    return null;
            }
        }
    }
}