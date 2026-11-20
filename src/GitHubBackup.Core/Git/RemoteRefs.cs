namespace GitHubBackup.Core.Git;

/// <summary>
/// Holds what one <c>ls-remote</c> request returns from the server: the branches and tags and the default branch.
/// </summary>
/// <param name="Refs">The server's branches and tags; other server refs, such as pull requests, are left out.</param>
/// <param name="DefaultBranch">
/// The branch the server's <c>HEAD</c> points at, or <see langword="null"/> when <c>HEAD</c> does not resolve to an
/// existing branch, as in an empty repository, or names a branch that is empty, starts with <c>-</c> or contains a
/// control character; a server-supplied name is never returned in that case.
/// </param>
public sealed record RemoteRefs(RefSnapshot Refs, string? DefaultBranch);
