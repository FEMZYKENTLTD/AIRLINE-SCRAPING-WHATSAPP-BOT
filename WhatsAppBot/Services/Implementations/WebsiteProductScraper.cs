using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Services.Interfaces;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// WebsiteProductScraper (legacy placeholder).
    ///
    /// WHY THIS EXISTS:
    /// - Your project still contains this file/class
    /// - It was written for an older interface (returning Product)
    /// - After we upgraded IProductScraper to return ScrapedItem, it broke compilation
    ///
    /// WHAT THIS DOES NOW:
    /// - Implements the new interface properly so the solution builds
    /// - Returns an empty list (safe) unless you later decide to use it
    ///
    /// NOTE:
    /// For your airline scraping "product" pipeline, you'll primarily use:
    /// - AirlineProductScraper (crawler + extractor)
    /// - ConfigDrivenHtmlProductScraper (selector-based page scraping)
    /// </summary>
    public class WebsiteProductScraper : IProductScraper
    {
        private readonly ILogger<WebsiteProductScraper> _logger;

        public WebsiteProductScraper(ILogger<WebsiteProductScraper> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Unique key for this scraper source.
        /// </summary>
        public string SourceKey => "website";

        /// <summary>
        /// Placeholder implementation:
        /// Returns empty list so builds/testing can proceed.
        /// </summary>
        public Task<List<ScrapedItem>> ScrapeAsync(CancellationToken ct)
        {
            _logger.LogInformation("WebsiteProductScraper is currently a placeholder and returns 0 items.");
            return Task.FromResult(new List<ScrapedItem>());
        }
    }
}
