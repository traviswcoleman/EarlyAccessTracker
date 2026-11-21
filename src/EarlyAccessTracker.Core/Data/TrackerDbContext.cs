using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EarlyAccessTracker.Core.Data;

public sealed class TrackerDbContext(DbContextOptions<TrackerDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<StatusHistory> StatusHistory => Set<StatusHistory>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // Everything is stored as UTC; make sure values come back marked as UTC.
        c.Properties<DateTime>().HaveConversion<UtcConverter>();
        c.Properties<DateTime?>().HaveConversion<UtcConverter>();
    }

    private sealed class UtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Game>(e =>
        {
            e.HasKey(g => g.AppId);
            e.Property(g => g.AppId).ValueGeneratedNever();
            e.Property(g => g.AlertStatus).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(g => g.AlertStatus);
        });

        b.Entity<StatusHistory>(e =>
        {
            e.HasIndex(h => new { h.AppId, h.ObservedAt });
            e.HasIndex(h => h.SyncRunId);
        });

        b.Entity<SyncRun>(e =>
        {
            e.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(16);
            e.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(r => r.StartedAt);
        });
    }
}

/// <summary>Used only by `dotnet ef` at design time.</summary>
public sealed class TrackerDbContextDesignFactory : IDesignTimeDbContextFactory<TrackerDbContext>
{
    public TrackerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TrackerDbContext>().UseSqlite("Data Source=design.db").Options);
}
