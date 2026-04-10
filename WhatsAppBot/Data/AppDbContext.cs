using Microsoft.EntityFrameworkCore;
using WhatsAppBot.Models;

namespace WhatsAppBot.Data
{
    /// <summary>
    /// Main EF Core DB context.
    /// Stores:
    /// - Chat logs
    /// - Products (scraped items)
    /// - Product images
    /// </summary>
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<ChatLog> ChatLogs { get; set; } = null!;
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<ProductImage> ProductImages { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ChatLog>(entity =>
            {
                entity.HasIndex(e => e.PhoneNumber);
                entity.HasIndex(e => e.Timestamp);
            });

            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasIndex(e => e.Sku).IsUnique();
                entity.HasIndex(e => e.Source);
                entity.HasIndex(e => e.LastSyncedAtUtc);

                entity.Property(e => e.Sku).HasMaxLength(80);
                entity.Property(e => e.Name).HasMaxLength(255);
                entity.Property(e => e.Source).HasMaxLength(50);
                entity.Property(e => e.Currency).HasMaxLength(10);
                entity.Property(e => e.SourceUrl).HasMaxLength(1000);
            });

            modelBuilder.Entity<ProductImage>(entity =>
            {
                entity.HasIndex(e => e.ProductId);
                entity.Property(e => e.Url).HasMaxLength(1200);

                entity.HasOne(e => e.Product)
                      .WithMany(p => p.Images)
                      .HasForeignKey(e => e.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
