using Microsoft.EntityFrameworkCore;
using WhatsAppBot.Models;
using WhatsAppBot.Models.Passengers;
using WhatsAppBot.Models.Payments;
using WhatsAppBot.Models.Reservations;
using WhatsAppBot.Models.Learning;

namespace WhatsAppBot.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<ChatLog> ChatLogs { get; set; } = null!;
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<ProductImage> ProductImages { get; set; } = null!;
        public DbSet<Reservation> Reservations { get; set; } = null!;
        public DbSet<PassengerInfo> Passengers { get; set; } = null!;
        public DbSet<PaymentRecord> Payments { get; set; } = null!;
        public DbSet<KnowledgeEntry> KnowledgeEntries { get; set; } = null!;
        public DbSet<UserPreference> UserPreferences { get; set; } = null!;
        public DbSet<ConversationInsight> ConversationInsights { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ChatLog
            modelBuilder.Entity<ChatLog>(entity =>
            {
                entity.HasIndex(e => e.PhoneNumber);
                entity.HasIndex(e => e.Timestamp);
            });

            // Product
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

            // ProductImage
            modelBuilder.Entity<ProductImage>(entity =>
            {
                entity.HasIndex(e => e.ProductId);
                entity.Property(e => e.Url).HasMaxLength(1200);
                entity.HasOne(e => e.Product)
                      .WithMany(p => p.Images)
                      .HasForeignKey(e => e.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Reservation
            modelBuilder.Entity<Reservation>(entity =>
            {
                entity.HasIndex(e => e.ReservationCode).IsUnique();
                entity.HasIndex(e => e.PhoneNumber);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAtUtc);

                entity.Property(e => e.ReservationCode).HasMaxLength(30);
                entity.Property(e => e.PhoneNumber).HasMaxLength(50);
                entity.Property(e => e.AirlineKey).HasMaxLength(50);
                entity.Property(e => e.FromAirport).HasMaxLength(5);
                entity.Property(e => e.ToAirport).HasMaxLength(5);
                entity.Property(e => e.Currency).HasMaxLength(5);
                entity.Property(e => e.BookingMethod).HasMaxLength(30);
                entity.Property(e => e.BookingUrl).HasMaxLength(2000);

                entity.HasOne(e => e.Passenger)
                      .WithMany()
                      .HasForeignKey(e => e.PassengerId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // Passenger
            modelBuilder.Entity<PassengerInfo>(entity =>
            {
                entity.HasIndex(e => e.PhoneNumber);
                entity.Property(e => e.FullName).HasMaxLength(200);
                entity.Property(e => e.PassportNumber).HasMaxLength(50);
                entity.Property(e => e.Email).HasMaxLength(255);
            });

            // Payment
            modelBuilder.Entity<PaymentRecord>(entity =>
            {
                entity.HasIndex(e => e.ReservationCode);
                entity.HasIndex(e => e.PhoneNumber);
                entity.HasIndex(e => e.ExternalPaymentId);
                entity.Property(e => e.ReservationCode).HasMaxLength(30);
                entity.Property(e => e.Currency).HasMaxLength(5);
                entity.Property(e => e.Provider).HasMaxLength(30);
                entity.Property(e => e.ExternalPaymentId).HasMaxLength(200);
            });

            modelBuilder.Entity<KnowledgeEntry>(entity =>
            {
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.AirlineKey);
                entity.HasIndex(e => e.Source);
                entity.HasIndex(e => e.IsActive);
                entity.Property(e => e.Category).HasMaxLength(100);
                entity.Property(e => e.Question).HasMaxLength(500);
                entity.Property(e => e.Source).HasMaxLength(50);
                entity.Property(e => e.AirlineKey).HasMaxLength(50);
            });

            modelBuilder.Entity<UserPreference>(entity =>
            {
                entity.HasIndex(e => new { e.PhoneNumber, e.PreferenceKey });
                entity.Property(e => e.PhoneNumber).HasMaxLength(50);
                entity.Property(e => e.PreferenceKey).HasMaxLength(100);
            });

            modelBuilder.Entity<ConversationInsight>(entity =>
            {
                entity.HasIndex(e => e.PhoneNumber);
                entity.HasIndex(e => e.CreatedAtUtc);
                entity.Property(e => e.PhoneNumber).HasMaxLength(50);
                entity.Property(e => e.TopicDetected).HasMaxLength(100);
            });
        }
    }
}