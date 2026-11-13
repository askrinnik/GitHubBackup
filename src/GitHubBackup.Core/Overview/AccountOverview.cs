namespace GitHubBackup.Core.Overview;

/// <summary>
/// Describes one tracked account of <c>backup-config.json</c> and the repositories listed under it.
/// </summary>
/// <param name="Login">The login of the account, as parsed from <paramref name="Url"/>.</param>
/// <param name="Url">The account URL as written in the file.</param>
/// <param name="Repositories">The repositories of the account in file order; empty when the account lists none.</param>
public sealed record AccountOverview(string Login, string Url, IReadOnlyList<RepositoryOverviewEntry> Repositories);
