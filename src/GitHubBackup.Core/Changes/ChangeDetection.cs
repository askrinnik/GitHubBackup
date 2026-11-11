using GitHubBackup.Core.Git;

namespace GitHubBackup.Core.Changes;

/// <summary>
/// The outcome of <see cref="IChangeDetector.DetectAsync"/>.
/// </summary>
/// <param name="Decision">Whether a new archive is needed.</param>
/// <param name="Reason">Why the decision was made.</param>
/// <param name="Branch">
/// The branch the clone has checked out, or <see langword="null"/> when the server repository is empty.
/// </param>
/// <param name="BranchFallback">
/// Whether the requested branch is missing on the server and the default branch is used instead; the caller reports
/// this as a warning.
/// </param>
/// <param name="Refs">The branches and tags of the server, as the clone holds them after synchronization.</param>
public sealed record ChangeDetection(
    ChangeDecision Decision, ChangeReason Reason, string? Branch, bool BranchFallback, RefSnapshot Refs);
