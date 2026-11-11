using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// What an <see cref="IDiscoveryDecisionHandler"/> shows the user about one new repository: its GitHub attributes and
/// where the entry proposed for it keeps its archives.
/// </summary>
/// <remarks>The proposed entry has the name and the id of the repository, no branch and no <c>path</c>.</remarks>
/// <param name="Repository">The repository as GitHub reports it.</param>
/// <param name="AccountLogin">The login of the tracked account it belongs to, or <see langword="null"/> for a standalone repository.</param>
/// <param name="ArchiveFolder">The absolute archive folder the proposed entry resolves to.</param>
public sealed record NewRepositoryProposal(GitHubRepository Repository, string? AccountLogin, string ArchiveFolder);
