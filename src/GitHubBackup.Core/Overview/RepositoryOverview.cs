namespace GitHubBackup.Core.Overview;

/// <summary>
/// Describes the repositories of <c>backup-config.json</c>: the tracked accounts and the standalone repositories.
/// </summary>
/// <param name="Accounts">The tracked accounts in file order.</param>
/// <param name="StandaloneRepositories">The standalone repositories in file order.</param>
public sealed record RepositoryOverview(
    IReadOnlyList<AccountOverview> Accounts, IReadOnlyList<RepositoryOverviewEntry> StandaloneRepositories)
{
    /// <summary>Gets whether the configuration lists neither an account nor a standalone repository.</summary>
    public bool IsEmpty => Accounts.Count == 0 && StandaloneRepositories.Count == 0;
}
