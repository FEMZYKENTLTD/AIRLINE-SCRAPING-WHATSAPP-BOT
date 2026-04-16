using System.Threading;
using System.Threading.Tasks;

namespace WhatsAppBot.Services.Media
{
    /// <summary>
    /// Interface for image understanding (computer vision).
    /// Uses gpt-4o which can read/analyze images.
    ///
    /// Use cases:
    /// - User sends a photo of a document → bot reads it
    /// - User sends screenshot of flight → bot extracts info
    /// - User sends image → bot describes it
    /// </summary>
    public interface IVisionService
    {
        /// <summary>Analyze an image from URL and return description.</summary>
        Task<string?> AnalyzeImageFromUrlAsync(
            string imageUrl, string? prompt = null, CancellationToken ct = default);

        /// <summary>Analyze an image from bytes and return description.</summary>
        Task<string?> AnalyzeImageAsync(
            byte[] imageBytes, string? prompt = null, CancellationToken ct = default);
    }
}