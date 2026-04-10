using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppBot.Models;

namespace WhatsAppBot.Services.Interfaces
{
    public interface IProductCatalogService
    {
        Task<List<Product>> GetLatestProductsAsync(int take = 10);
        Task<Product?> GetBySkuAsync(string sku);
        Task<List<Product>> SearchAsync(string query, int take = 10);
    }
}
