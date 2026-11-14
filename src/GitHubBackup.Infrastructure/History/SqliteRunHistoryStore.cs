using GitHubBackup.Core.History;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.History;

/// <summary>
/// Keeps the run history in the SQLite database of <see cref="HistoryOptions.DatabasePath"/>, one context per
/// operation.
/// </summary>
/// <param name="contextFactory">Creates the context of the database.</param>
/// <param name="options">The settings with the full path of the database, named in errors.</param>
/// <param name="secretMasker">
/// Masks secrets in error texts before they are stored: an error comes from git or HTTP output, and the database must
/// never hold the token whatever the caller passes.
/// </param>
internal sealed class SqliteRunHistoryStore(
    IDbContextFactory<HistoryDbContext> contextFactory,
    IOptions<HistoryOptions> options,
    ISecretMasker secretMasker) : IRunHistoryStore
{
    /// <inheritdoc />
    public async Task AddAsync(RunHistoryEntry run, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            context.Runs.Add(ToEntity(run));

            // One SaveChanges is one transaction, so a run is stored with all its rows or not at all.
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqliteException)
        {
            throw new RunHistoryException(
                options.Value.DatabasePath,
                $"The run {run.RunId} cannot be recorded. {exception.GetBaseException().Message}",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<RunHistoryEntry?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await context.Runs
                .AsNoTracking()
                .Include(run => run.Items.OrderBy(item => item.Id))
                .Include(run => run.DiscoveryEvents.OrderBy(discoveryEvent => discoveryEvent.Id))
                .AsSplitQuery()
                .SingleOrDefaultAsync(run => run.RunId == runId, cancellationToken);
            return entity is null ? null : ToEntry(entity);
        }
        catch (SqliteException exception)
        {
            throw new RunHistoryException(
                options.Value.DatabasePath, $"The run {runId} cannot be read. {exception.Message}", exception);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The mode, the dry-run flag and the exit code are filtered in the database. The period is filtered after
    /// reading: the provider stores a <see cref="DateTimeOffset"/> as text with its offset and cannot compare
    /// instants in SQL. Runs never overlap, so the order of recording is the order of starting.
    /// </remarks>
    public async Task<IReadOnlyList<RunHistoryEntry>> QueryAsync(
        RunHistoryQuery query, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var runs = context.Runs.AsNoTracking();
            if (query.Mode is { } mode)
            {
                runs = runs.Where(run => run.Mode == mode);
            }

            if (query.IsDryRun is { } isDryRun)
            {
                runs = runs.Where(run => run.IsDryRun == isDryRun);
            }

            if (query.ExitCode is { } exitCode)
            {
                runs = runs.Where(run => run.ExitCode == exitCode);
            }

            var entities = await runs
                .OrderByDescending(run => run.Id)
                .Include(run => run.Items.OrderBy(item => item.Id))
                .Include(run => run.DiscoveryEvents.OrderBy(discoveryEvent => discoveryEvent.Id))
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            return
            [
                .. entities
                    .Where(run => (query.From is not { } from || run.StartedAt >= from)
                        && (query.To is not { } to || run.StartedAt < to))
                    .Select(ToEntry),
            ];
        }
        catch (SqliteException exception)
        {
            throw new RunHistoryException(
                options.Value.DatabasePath, $"The runs cannot be read. {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Maps a run to the rows that store it.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>The run row with its item and event rows.</returns>
    private RunEntity ToEntity(RunHistoryEntry run)
    {
        var entity = new RunEntity
        {
            RunId = run.RunId,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Mode = run.Mode,
            IsDryRun = run.IsDryRun,
            AccountFilter = run.AccountFilter,
            RepositoryFilter = run.RepositoryFilter,
            ExitCode = run.ExitCode,
        };

        entity.Items.AddRange(run.Items.Select(item => new RunItemEntity
        {
            Repository = item.Repository,
            Target = item.Target,
            Status = item.Status,
            Duration = item.Duration,
            Branch = item.Branch,
            HeadCommit = item.HeadCommit,
            ArchiveFileName = item.ArchiveFileName,
            ChangedRefs = [.. item.ChangedRefs],
            Error = item.Error is null ? null : secretMasker.Mask(item.Error),
        }));

        entity.DiscoveryEvents.AddRange(run.DiscoveryEvents.Select(discoveryEvent => new DiscoveryEventEntity
        {
            Repository = discoveryEvent.Repository,
            PreviousFullName = discoveryEvent.PreviousFullName,
            Kind = discoveryEvent.Kind,
            Decision = discoveryEvent.Decision,
            DecidedBy = discoveryEvent.DecidedBy,
        }));

        return entity;
    }

    /// <summary>
    /// Maps the rows of a run back to the run.
    /// </summary>
    /// <param name="entity">The run row with its item and event rows in the order they were added.</param>
    /// <returns>The run.</returns>
    private static RunHistoryEntry ToEntry(RunEntity entity) =>
        new(entity.RunId, entity.StartedAt, entity.FinishedAt, entity.Mode, entity.IsDryRun, entity.ExitCode)
        {
            AccountFilter = entity.AccountFilter,
            RepositoryFilter = entity.RepositoryFilter,
            Items =
            [
                .. entity.Items.Select(item =>
                    new RunHistoryItem(item.Repository, item.Target, item.Status, item.Duration)
                    {
                        Branch = item.Branch,
                        HeadCommit = item.HeadCommit,
                        ArchiveFileName = item.ArchiveFileName,
                        ChangedRefs = [.. item.ChangedRefs],
                        Error = item.Error,
                    }),
            ],
            DiscoveryEvents =
            [
                .. entity.DiscoveryEvents.Select(discoveryEvent =>
                    new DiscoveryHistoryEvent(discoveryEvent.Repository, discoveryEvent.Kind)
                    {
                        PreviousFullName = discoveryEvent.PreviousFullName,
                        Decision = discoveryEvent.Decision,
                        DecidedBy = discoveryEvent.DecidedBy,
                    }),
            ],
        };
}
