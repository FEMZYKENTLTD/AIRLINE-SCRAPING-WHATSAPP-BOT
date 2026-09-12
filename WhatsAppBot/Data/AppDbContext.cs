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

        // ── Existing entities ────────────────────────────────────────────
        public DbSet<ChatLog> ChatLogs { get; set; } = null!;
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<ProductImage> ProductImages { get; set; } = null!;
        public DbSet<Reservation> Reservations { get; set; } = null!;
        public DbSet<PassengerInfo> Passengers { get; set; } = null!;
        public DbSet<PaymentRecord> Payments { get; set; } = null!;
        public DbSet<KnowledgeEntry> KnowledgeEntries { get; set; } = null!;
        public DbSet<UserPreference> UserPreferences { get; set; } = null!;
        public DbSet<ConversationInsight> ConversationInsights { get; set; } = null!;

        // ── New entities ─────────────────────────────────────────────────
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<ChannelIdentity> ChannelIdentities { get; set; } = null!;
        public DbSet<AppSession> AppSessions { get; set; } = null!;
        public DbSet<Message> Messages { get; set; } = null!;
        public DbSet<ServiceRequest> ServiceRequests { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<AiInteractionLog> AiInteractionLogs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── ChatLog ──────────────────────────────────────────────────
            modelBuilder.Entity<ChatLog>(entity =>
            {
                entity.HasIndex(e => e.PhoneNumber);
                entity.HasIndex(e => e.Timestamp);
            });

            // ── Product ──────────────────────────────────────────────────
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

            // ── ProductImage ─────────────────────────────────────────────
            modelBuilder.Entity<ProductImage>(entity =>
            {
                entity.HasIndex(e => e.ProductId);
                entity.Property(e => e.Url).HasMaxLength(1200);
                entity.HasOne(e => e.Product)
                      .WithMany(p => p.Images)
                      .HasForeignKey(e => e.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ── Reservation ──────────────────────────────────────────────
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

            // ── PassengerInfo ────────────────────────────────────────────
            modelBuilder.Entity<PassengerInfo>(entity =>
            {
                entity.HasIndex(e => e.PhoneNumber);
                entity.Property(e => e.FullName).HasMaxLength(200);
                entity.Property(e => e.PassportNumber).HasMaxLength(50);
                entity.Property(e => e.Email).HasMaxLength(255);
            });

            // ── PaymentRecord ────────────────────────────────────────────
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

            // ── KnowledgeEntry ───────────────────────────────────────────
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

            // ── UserPreference ───────────────────────────────────────────
            modelBuilder.Entity<UserPreference>(entity =>
            {
                entity.HasIndex(e => new { e.PhoneNumber, e.PreferenceKey });
                entity.Property(e => e.PhoneNumber).HasMaxLength(50);
                entity.Property(e => e.PreferenceKey).HasMaxLength(100);
            });

            // ── ConversationInsight ──────────────────────────────────────
            modelBuilder.Entity<ConversationInsight>(entity =>
            {
                entity.HasIndex(e => e.PhoneNumber);
                entity.HasIndex(e => e.CreatedAtUtc);
                entity.Property(e => e.PhoneNumber).HasMaxLength(50);
                entity.Property(e => e.TopicDetected).HasMaxLength(100);
            });

            // ══════════════════════════════════════════════════════════════
            // NEW: Multi-Channel User Management
            // ══════════════════════════════════════════════════════════════

            // ── User ─────────────────────────────────────────────────────
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.ExternalId).IsUnique();
                entity.HasIndex(e => e.Email);
                entity.HasIndex(e => e.PhoneNumber);
                entity.HasIndex(e => e.Status);
            });

            // ── ChannelIdentity ──────────────────────────────────────────
            modelBuilder.Entity<ChannelIdentity>(entity =>
            {
                // A user can only have one identity per channel+provider_user_id
                entity.HasIndex(e => new { e.Channel, e.ProviderUserId }).IsUnique();
                entity.HasIndex(e => e.UserId);

                entity.HasOne(e => e.User)
                      .WithMany(u => u.ChannelIdentities)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ── AppSession ───────────────────────────────────────────────
            modelBuilder.Entity<AppSession>(entity =>
            {
                entity.HasIndex(e => e.SessionId).IsUnique();
                entity.HasIndex(e => e.Channel);
                entity.HasIndex(e => e.LastActivityAtUtc);
                entity.HasIndex(e => e.ExpiresAtUtc);
                entity.HasIndex(e => e.UserId);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.ChannelIdentity)
                      .WithMany()
                      .HasForeignKey(e => e.ChannelIdentityId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // ── Message ──────────────────────────────────────────────────
            modelBuilder.Entity<Message>(entity =>
            {
                entity.HasIndex(e => e.ProviderMessageId);
                entity.HasIndex(e => e.Channel);
                entity.HasIndex(e => e.Direction);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.CorrelationId);

                entity.HasOne(e => e.Session)
                      .WithMany()
                      .HasForeignKey(e => e.SessionId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // ── ServiceRequest ───────────────────────────────────────────
            modelBuilder.Entity<ServiceRequest>(entity =>
            {
                entity.HasIndex(e => e.RequestCode).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.RequestType);
                entity.HasIndex(e => e.Channel);
                entity.HasIndex(e => e.CreatedAtUtc);
                entity.HasIndex(e => e.ReservationCode);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Session)
                      .WithMany()
                      .HasForeignKey(e => e.SessionId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // ── AuditLog ─────────────────────────────────────────────────
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasIndex(e => e.Action);
                entity.HasIndex(e => e.EntityType);
                entity.HasIndex(e => e.ActorId);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.CorrelationId);
            });

            // ── AiInteractionLog ─────────────────────────────────────────
            modelBuilder.Entity<AiInteractionLog>(entity =>
            {
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Provider);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.CorrelationId);
                entity.HasIndex(e => e.Success);
            });
        }
    }
}
