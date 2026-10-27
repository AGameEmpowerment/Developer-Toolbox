using Microsoft.EntityFrameworkCore;

namespace DeveloperToolbox.Example.Data;

/// <summary>Defines the same simple schema for both relational providers.</summary>
public abstract class SampleContext(DbContextOptions options) : DbContext(options)
{
    /// <summary>Messages persisted by HTTP, CLI, and queue examples.</summary>
    public DbSet<SampleMessage> Messages => Set<SampleMessage>();

    /// <summary>Applies portable constraints without raw SQL.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var message = modelBuilder.Entity<SampleMessage>();
        message.ToTable("Messages");
        message.HasKey(item => item.Id);
        message.Property(item => item.Id).HasMaxLength(128);
        message.Property(item => item.Text).HasMaxLength(4096).IsRequired();
        message.Property(item => item.Source).HasMaxLength(64).IsRequired();
        message.HasIndex(item => item.CreatedAtUtc);
    }
}

/// <summary>EF Core context using the SQL Server provider.</summary>
public sealed class SqlServerSampleContext(DbContextOptions<SqlServerSampleContext> options) : SampleContext(options);

/// <summary>EF Core context using the PostgreSQL provider.</summary>
public sealed class PostgresSampleContext(DbContextOptions<PostgresSampleContext> options) : SampleContext(options);
