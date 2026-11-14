using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// The row of a configured repository that GitHub does not report; it only informs, because the entry becomes
/// unavailable without a question and nothing of it is deleted.
/// </summary>
/// <param name="tracked">The configuration entry.</param>
public sealed class MissingDiscoveryRowViewModel(TrackedRepository tracked)
    : DiscoveryRowViewModel(tracked.FullName, tracked.Id, tracked.AccountLogin, null)
{
    /// <inheritdoc />
    public override string Kind => "Missing";

    /// <inheritdoc />
    public override string Note => "Not found on GitHub; marked unavailable, nothing is deleted";

    /// <inheritdoc />
    public override bool IsActionable => false;
}
