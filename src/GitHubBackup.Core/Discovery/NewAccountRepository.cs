using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// A repository of a tracked account that the configuration does not list yet.
/// </summary>
/// <param name="AccountLogin">The login of the tracked account, as written in its configured URL.</param>
/// <param name="Repository">The repository as GitHub reports it.</param>
public sealed record NewAccountRepository(string AccountLogin, GitHubRepository Repository);
