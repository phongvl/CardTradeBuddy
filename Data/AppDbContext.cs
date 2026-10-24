using System.Text.Json;
using CardTradeBuddy.Services;
using Microsoft.EntityFrameworkCore;

namespace CardTradeBuddy.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Card> Cards => Set<Card>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserPreference>(e =>
        {
            e.ToTable("user_preferences");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.Language).HasMaxLength(10);
        });

        modelBuilder.Entity<UserProfile>(e =>
        {
            e.ToTable("user_profiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.DisclaimerVersion).HasMaxLength(32);
        });

        modelBuilder.Entity<Album>(e =>
        {
            e.ToTable("albums");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.UserId, "IX_albums_UserId");
            // One default album per user, however many albums they get later.
            e.HasIndex(x => x.UserId, "IX_albums_UserId_default").IsUnique().HasFilter("\"IsDefault\" = true");
        });

        modelBuilder.Entity<Card>(e =>
        {
            e.ToTable("cards");
            e.HasKey(x => x.Id);
            // A card is in exactly one album; deleting an album deletes its cards.
            e.HasOne(x => x.Album).WithMany(a => a.Cards).HasForeignKey(x => x.AlbumId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.AlbumId);
            e.Property(x => x.CardId).HasMaxLength(64);
            e.Property(x => x.LocalId).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Rarity).HasMaxLength(64);
            e.Property(x => x.Illustrator).HasMaxLength(200);
            e.Property(x => x.SetId).HasMaxLength(64);
            e.Property(x => x.SetName).HasMaxLength(200);
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.SetLogoUrl).HasMaxLength(500);
            e.Property(x => x.Stage).HasMaxLength(64);
            e.Property(x => x.Finish).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.EstimatedPriceEur).HasPrecision(12, 2);
            // The marketplace figures as received, one JSON document.
            e.Property(x => x.Pricing)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => v == null ? null : JsonSerializer.Deserialize<CardPricing>(v, (JsonSerializerOptions?)null));
        });
    }
}
