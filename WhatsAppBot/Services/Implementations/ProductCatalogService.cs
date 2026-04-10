using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppBot.Data;
using WhatsAppBot.Models;
using WhatsAppBot.Services.Interfaces;

namespace WhatsAppBot.Services.Implementations
{
    /// <summary>
    /// DB-backed product catalog access layer.
    /// Uses EF.Functions.Like to avoid case-insensitive Contains analyzer warnings
    /// AND to keep it provider-friendly (SQLite).
    /// </summary>
    public class ProductCatalogService(AppDbContext db) : IProductCatalogService
    {
        private readonly AppDbContext _db = db;

        public Task<List<Product>> GetLatestProductsAsync(int take = 10)
        {
            return _db.Products.AsNoTracking()
                .Include(p => p.Images)
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.LastSyncedAtUtc)
                .Take(take)
                .ToListAsync();
        }

        public Task<Product?> GetBySkuAsync(string sku)
        {
            sku = (sku ?? string.Empty).Trim();

            return _db.Products.AsNoTracking()
                .Include(p => p.Images)
                .Where(p => p.IsActive && p.Sku == sku)
                .FirstOrDefaultAsync();
        }

        public Task<List<Product>> SearchAsync(string query, int take = 10)
        {
            query = (query ?? string.Empty).Trim();

            // SQLite LIKE is usually case-insensitive for ASCII depending on collation,
            // and this stays translated to SQL (fast + safe).
            var like = $"%{query}%";

            return _db.Products.AsNoTracking()
                .Include(p => p.Images)
                .Where(p => p.IsActive &&
                            (EF.Functions.Like(p.Name, like) ||
                             (p.Description != null && EF.Functions.Like(p.Description, like)) ||
                             EF.Functions.Like(p.Sku, like)))
                .OrderByDescending(p => p.LastSyncedAtUtc)
                .Take(take)
                .ToListAsync();
        }
    }
}
