namespace GitHubBackup.Core.Git;

/// <summary>
/// Describes the local state of a clone that an update would overwrite, as <see cref="IGitClient.GetWorkingCopyStatusAsync"/>
/// reads it without the network.
/// </summary>
/// <param name="Branch">The checked-out branch, or <see langword="null"/> when <c>HEAD</c> is detached.</param>
/// <param name="HasCommits">Whether <c>HEAD</c> points at a commit; a clone of an empty repository has none.</param>
/// <param name="HasUncommittedChanges">
/// Whether a tracked file, the index or a submodule differs from <c>HEAD</c>, including untracked content inside a
/// submodule.
/// </param>
/// <param name="HasUntrackedFiles">Whether the working tree holds files git does not track and does not ignore.</param>
/// <param name="UnpushedCommits">
/// The number of commits of <c>HEAD</c> that are not in <c>origin/&lt;branch&gt;</c>, or, when the clone has no such
/// remote-tracking branch, in no remote-tracking branch of <c>origin</c>.
/// </param>
public sealed record WorkingCopyStatus(
    string? Branch, bool HasCommits, bool HasUncommittedChanges, bool HasUntrackedFiles, int UnpushedCommits)
{
    /// <summary>Gets a value indicating whether <c>HEAD</c> is detached from every branch.</summary>
    public bool IsDetached => Branch is null;
}
