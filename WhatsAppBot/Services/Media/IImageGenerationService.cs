using System.Threading;
using System.Threading.Tasks;

namespace WhatsAppBot.Services.Media
{
    /// <summary>
    /// Interface for AI image generation.
    /// Implementation uses Azure OpenAI gpt-image-1 (or dall-e-2 fallback).
    /// Used when users ask the bot to "generate", "create", or "draw" an image.
    /// </summary>
    public interface IImageGenerationService
    {
        /// <summary>
        /// Generate an image from a text prompt.
        /// Returns a public URL to the generated image.
        /// </summary>
        Task<ImageGenerationResult> GenerateImageAsync(
            string prompt,
            string size = "1024x1024",
            CancellationToken ct = default);
    }

    public class ImageGenerationResult
    {
        public bool Success { get; set; }
        public string? ImageUrl { get; set; }
        public string? Base64Data { get; set; }
        public string? ErrorMessage { get; set; }
        public string ModelUsed { get; set; } = string.Empty;
    }
}