namespace GitHubBackup.Core.Clones;

/// <summary>
/// The outcome of <see cref="ICloneGuard.CheckAsync"/>: the local work found in a clone that an update or a move would
/// overwrite.
/// </summary>
/// <param name="CloneFolder">The checked clone folder.</param>
/// <param name="Problems">
/// The problems in a fixed order: uncommitted changes, untracked files, the branch, unpushed commits, unpushed commits
/// of the branch an update would reset; empty when the clone may be changed.
/// </param>
public sealed record CloneCheckResult(string CloneFolder, IReadOnlyList<string> Problems)
{
    /// <summary>The problem of a clone with uncommitted changes, in a tracked file, the index or a submodule.</summary>
    public const string UncommittedChanges = "uncommitted changes";

    /// <summary>The problem of a clone with files that git neither tracks nor ignores.</summary>
    public const string UntrackedFiles = "untracked files";

    /// <summary>
    /// Gets the branch an update would check out, as the check determined it, or <see langword="null"/> when the branch
    /// was not checked; <see cref="ICloneGuard.CheckLocalAsync"/> takes it to repeat the check without the network.
    /// </summary>
    public string? ExpectedBranch { get; init; }

    /// <summary>Gets a value indicating whether the clone holds no local work and may be changed.</summary>
    public bool Passed => Problems.Count == 0;

    /// <summary>
    /// Gets the sentence that names the clone folder and every problem, used as the reason of the failed item.
    /// </summary>
    public string Message =>
        $"The clone '{CloneFolder}' has local changes: {string.Join(", ", Problems)}. "
        + "Commit and push them or switch the branch, then run the backup again.";

    /// <summary>
    /// Returns the problem of a clone that has another branch checked out.
    /// </summary>
    /// <param name="actual">The checked-out branch.</param>
    /// <param name="expected">The branch an update would check out.</param>
    /// <returns>The problem text.</returns>
    public static string OtherBranch(string actual, string expected) => $"branch '{actual}' instead of '{expected}'";

    /// <summary>
    /// Returns the problem of a clone whose <c>HEAD</c> is detached.
    /// </summary>
    /// <param name="expected">The branch an update would check out.</param>
    /// <returns>The problem text.</returns>
    public static string DetachedHead(string expected) => $"detached HEAD instead of branch '{expected}'";

    /// <summary>
    /// Returns the problem of a clone with commits the server does not have.
    /// </summary>
    /// <param name="count">The number of unpushed commits, at least one.</param>
    /// <returns>The problem text.</returns>
    public static string UnpushedCommits(int count) => $"{count} unpushed commit(s)";

    /// <summary>
    /// Returns the problem of a local branch, other than the checked-out one, that an update would reset.
    /// </summary>
    /// <param name="count">The number of its unpushed commits, at least one.</param>
    /// <param name="branch">The branch.</param>
    /// <returns>The problem text.</returns>
    public static string UnpushedBranchCommits(int count, string branch) =>
        $"{count} unpushed commit(s) on branch '{branch}'";
}
