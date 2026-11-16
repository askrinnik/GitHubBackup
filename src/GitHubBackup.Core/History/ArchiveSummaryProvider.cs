using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Cleanup;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.History;

/// <summary>
/// Combines the repository overview with the archive history of each repository, so the table and the archive list
/// of a repository count the same files.
/// </summary>
/// <param name="overviewProvider">Reads the repositories of the configuration with their archive folders.</param>
/// <param name="historyProvider">Reads the archives of one repository.</param>
/// <param name="stateStore">Reads the record order of the state file for the cleanup plan.</param>
/// <param name="runHistoryStore">Reads the latest run of every repository for the wiki status.</param>
/// <param name="nameBuilder">Recognises the archive files of the repository by their names for the cleanup plan.</param>
/// <param name="logger">The logger.</param>
public sealed class ArchiveSummaryProvider(
    IRepositoryOverviewProvider overviewProvider,
    IArchiveHistoryProvider historyProvider,
    IBackupStateStore stateStore,
    IRunHistoryStore runHistoryStore,
    IArchiveNameBuilder nameBuilder,
    ILogger<ArchiveSummaryProvider> logger) : IArchiveSummaryProvider
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RepositoryArchiveSummary>> LoadAsync(CancellationToken cancellationToken)
    {
        var overview = await overviewProvider.LoadAsync(cancellationToken);
        var repositories = overview.Accounts.SelectMany(account => account.Repositories)
            .Concat(overview.StandaloneRepositories)
            .ToList();
        var latestRuns = await LoadLatestRunsAsync(repositories, cancellationToken);
        var summaries = new List<RepositoryArchiveSummary>();
        foreach (var repository in repositories)
        {
            var summary = await LoadSummaryAsync(repository, cancellationToken);
            var latest = latestRuns is not null && latestRuns.TryGetValue(repository.FullName, out var run) ? run : null;
            summaries.Add(summary with { WikiStatus = WikiStatusResolver.Resolve(latest) });
        }

        return summaries;
    }

    /// <summary>
    /// Reads the latest run of every repository with one history query.
    /// </summary>
    /// <param name="repositories">The repositories of the table.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The latest runs by name, or <see langword="null"/> when the history cannot be read.</returns>
    private async Task<IReadOnlyDictionary<string, LatestRepositoryRun>?> LoadLatestRunsAsync(
        List<RepositoryOverviewEntry> repositories, CancellationToken cancellationToken)
    {
        try
        {
            return await runHistoryStore.GetLatestRepositoryRunsAsync(
                [.. repositories.Select(repository => repository.FullName)], cancellationToken);
        }
        catch (RunHistoryException exception)
        {
            ArchiveSummaryProviderLog.RunHistoryUnreadable(logger, exception);
            return null;
        }
    }

    /// <summary>
    /// Reads the archives of one repository with its cleanup plan and turns a failure to read its files into the error
    /// of its summary.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The summary with the history and the cleanup plan, or the error.</returns>
    private async Task<RepositoryArchiveSummary> LoadSummaryAsync(
        RepositoryOverviewEntry repository, CancellationToken cancellationToken)
    {
        try
        {
            var history = await historyProvider.LoadAsync(
                repository.ArchiveFolder, repository.RepositoryId, repository.FullName, cancellationToken);
            var state = await stateStore.LoadAsync(
                repository.ArchiveFolder, repository.RepositoryId, repository.FullName, cancellationToken);
            return new(repository, history, null, ArchiveCleanupPlanner.Plan(state, history, nameBuilder, repository.FullName));
        }
        catch (Exception exception) when (exception is BackupStateException or IOException or UnauthorizedAccessException)
        {
            return new(repository, null, exception);
        }
    }
}
