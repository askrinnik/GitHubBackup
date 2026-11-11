namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Reports what <see cref="IDiscoveryDecisionApplier"/> changed, for the host to show the user.
/// </summary>
/// <param name="Added">The <c>owner/name</c> of each repository added to the configuration.</param>
/// <param name="Skipped">The <c>owner/name</c> of each new repository left out of the configuration.</param>
/// <param name="Excluded">The <c>owner/name</c> of each new repository excluded by a mask.</param>
/// <param name="MarkedUnavailable">The <c>owner/name</c> of each entry marked unavailable.</param>
/// <param name="Reactivated">The <c>owner/name</c> of each unavailable entry that GitHub reports again.</param>
/// <param name="Renamed">The renamed repositories.</param>
/// <param name="ConfigSaved">Whether the configuration file was written.</param>
public sealed record DiscoveryApplyResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Excluded,
    IReadOnlyList<string> MarkedUnavailable,
    IReadOnlyList<string> Reactivated,
    IReadOnlyList<RenameOutcome> Renamed,
    bool ConfigSaved);
