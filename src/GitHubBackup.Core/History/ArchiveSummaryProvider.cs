using GitHubBackup.Core.Overview;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.History;

/// <summary>
/// Combines the repository overview with the archive history of each repository, so the table and the archive list
/// of a repository count the same files.
/// </summary>
/// <param name="overviewProvider">Reads the repositories of the configuration with their archive folders.</param>
/// <param name="historyProvider">Reads the archives of one repository.</param>
public sealed class ArchiveSummaryProvider(
    IRepositoryOverviewProvider overviewProvider, IArchiveHistoryProvider historyProvider) : IArchiveSummaryProvider
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
    /// Reads the archives of one repository and turns a failure to read its files into the error of its summary.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The summary with the history or the error.</returns>
    private async Task<RepositoryArchiveSummary> LoadSummaryAsync(
        RepositoryOverviewEntry repository, CancellationToken cancellationToken)
    {
        try
        {
            var history = await historyProvider.LoadAsync(
                repository.ArchiveFolder, repository.RepositoryId, repository.FullName, cancellationToken);
            return new(repository, history, null);
        }
        catch (Exception exception) when (exception is BackupStateException or IOException or UnauthorizedAccessException)
        {
            return new(repository, null, exception);
        }
    }
}
