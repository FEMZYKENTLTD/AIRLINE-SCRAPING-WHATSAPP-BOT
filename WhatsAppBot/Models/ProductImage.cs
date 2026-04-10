using System;

namespace WhatsAppBot.Models
{
    /// <summary>
    /// Stores publicly reachable image URLs for a Product.
    /// WhatsApp Cloud API requires images to be accessible by Meta servers (public HTTPS).
    /// </summary>
    public class ProductImage
    {
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;

        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Optional caption for the image (safe to keep nullable).
        /// </summary>
        public string? Caption { get; set; }

        /// <summary>
        /// True if this is the preferred image to show first.
        /// </summary>
        public bool IsPrimary { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
