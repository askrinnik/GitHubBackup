using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using Microsoft.EntityFrameworkCore;

namespace GitHubBackup.Infrastructure.History;

/// <summary>
/// Maps the run history to the SQLite database.
/// </summary>
/// <param name="options">The options of the context, with the connection string of the database.</param>
internal sealed class HistoryDbContext(DbContextOptions<HistoryDbContext> options) : DbContext(options)
{
    /// <summary>Gets the runs.</summary>
    public DbSet<RunEntity> Runs => Set<RunEntity>();

    /// <summary>Gets the items of all runs.</summary>
    public DbSet<RunItemEntity> RunItems => Set<RunItemEntity>();

    /// <summary>Gets the discovery events of all runs.</summary>
    public DbSet<DiscoveryEventEntity> DiscoveryEvents => Set<DiscoveryEventEntity>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Names keep the rows readable in any SQLite tool and survive a reordering of the enum members.
        configurationBuilder.Properties<RunMode>().HaveConversion<string>();
        configurationBuilder.Properties<BackupTarget>().HaveConversion<string>();
        configurationBuilder.Properties<BackupItemStatus>().HaveConversion<string>();
        configurationBuilder.Properties<DiscoveryEventKind>().HaveConversion<string>();
        configurationBuilder.Properties<DiscoveryEventDecision>().HaveConversion<string>();
        configurationBuilder.Properties<DecisionMaker>().HaveConversion<string>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<RunEntity>(run =>
        {
            run.HasIndex(entity => entity.RunId).IsUnique();
            run.HasMany(entity => entity.Items)
                .WithOne()
                .HasForeignKey(item => item.RunEntityId)
                .OnDelete(DeleteBehavior.Cascade);
            run.HasMany(entity => entity.DiscoveryEvents)
                .WithOne()
                .HasForeignKey(discoveryEvent => discoveryEvent.RunEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
