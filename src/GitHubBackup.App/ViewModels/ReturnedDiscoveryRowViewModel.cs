using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// The row of an unavailable repository that GitHub reports again; it only informs, because the entry becomes active
/// without a question.
/// </summary>
/// <param name="returned">The configuration entry and the repository as GitHub reports it.</param>
public sealed class ReturnedDiscoveryRowViewModel(ReturnedRepository returned)
    : DiscoveryRowViewModel(returned.Current.FullName, returned.Current.Id, returned.Tracked.AccountLogin, returned.Current)
{
    /// <inheritdoc />
    public override string Kind => "Returned";

    /// <inheritdoc />
    public override string Note => "Found on GitHub again; marked active";

    /// <inheritdoc />
    public override bool IsActionable => false;
}
