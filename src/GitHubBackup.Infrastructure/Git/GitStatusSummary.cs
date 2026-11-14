namespace GitHubBackup.Infrastructure.Git;

/// <summary>
/// The part of <c>git status --porcelain=v2 --branch</c> that <see cref="GitStatusParser"/> reads.
/// </summary>
/// <param name="Branch">The checked-out branch, or <see langword="null"/> when <c>HEAD</c> is detached.</param>
/// <param name="HasCommits">Whether <c>HEAD</c> points at a commit.</param>
/// <param name="HasChanges">Whether a tracked file, the index or a submodule differs from <c>HEAD</c>.</param>
/// <param name="HasUntrackedFiles">Whether there are files git neither tracks nor ignores.</param>
internal sealed record GitStatusSummary(string? Branch, bool HasCommits, bool HasChanges, bool HasUntrackedFiles);
