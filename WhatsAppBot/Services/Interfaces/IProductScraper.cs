using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Interfaces
{
    /// <summary>
    /// A scraper that returns normalized ScrapedItem objects for one source.
    /// </summary>
    public interface IProductScraper
    {
        /// <summary>
        /// Unique key identifying the scraper source (e.g., "arikair", "airpeace").
        /// </summary>
        string SourceKey { get; }

        /// <summary>
        /// Scrape and return normalized items.
        /// </summary>
        Task<List<ScrapedItem>> ScrapeAsync(CancellationToken ct);
    }
}
