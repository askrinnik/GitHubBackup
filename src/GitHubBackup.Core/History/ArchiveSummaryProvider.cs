using GitHubBackup.Core.Cleanup;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.History;

/// <summary>
/// Combines the repository overview with the archive history of each repository, so the table and the archive list
/// of a repository count the same files.
/// </summary>
/// <param name="overviewProvider">Reads the repositories of the configuration with their archive folders.</param>
/// <param name="historyProvider">Reads the archives of one repository.</param>
/// <param name="stateStore">Reads the record order of the state file for the cleanup plan.</param>
public sealed class ArchiveSummaryProvider(
    IRepositoryOverviewProvider overviewProvider,
    IArchiveHistoryProvider historyProvider,
    IBackupStateStore stateStore) : IArchiveSummaryProvider
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RepositoryArchiveSummary>> LoadAsync(CancellationToken cancellationToken)
    {
        var overview = await overviewProvider.LoadAsync(cancellationToken);
        var summaries = new List<RepositoryArchiveSummary>();
        foreach (var repository in overview.Accounts.SelectMany(account => account.Repositories)
            .Concat(overview.StandaloneRepositories))
        {
            summaries.Add(await LoadSummaryAsync(repository, cancellationToken));
        }

        return summaries;
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
            return new(repository, history, null, ArchiveCleanupPlanner.Plan(state, history));
        }
        catch (Exception exception) when (exception is BackupStateException or IOException or UnauthorizedAccessException)
        {
            return new(repository, null, exception);
        }
    }
}
