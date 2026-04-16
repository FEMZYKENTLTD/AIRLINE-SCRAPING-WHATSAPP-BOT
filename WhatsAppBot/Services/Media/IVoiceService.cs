using System.Threading;
using System.Threading.Tasks;

namespace WhatsAppBot.Services.Media
{
    /// <summary>
    /// Interface for voice/audio features.
    /// Uses Azure Speech Service (FREE - 5hrs/month).
    ///
    /// Features:
    /// - Text to Speech: Bot sends voice messages
    /// - Speech to Text: Process voice messages from users
    /// - Audio CAPTCHA solving
    /// </summary>
    public interface IVoiceService
    {
        /// <summary>Convert text to speech audio file. Returns file path.</summary>
        Task<string?> TextToSpeechAsync(string text, CancellationToken ct = default);

        /// <summary>Convert speech audio to text.</summary>
        Task<string?> SpeechToTextAsync(byte[] audioBytes, CancellationToken ct = default);

        /// <summary>Convert speech audio from URL to text.</summary>
        Task<string?> SpeechToTextFromUrlAsync(string audioUrl, CancellationToken ct = default);
    }
}