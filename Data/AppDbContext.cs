using Microsoft.EntityFrameworkCore;

namespace CardTradeBuddy.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

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
        });
    }
}
