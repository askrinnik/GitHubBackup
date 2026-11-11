namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Reports how one renamed repository was applied.
/// </summary>
/// <param name="OldFullName">The <c>owner/name</c> in the configuration before the rename.</param>
/// <param name="NewFullName">The <c>owner/name</c> GitHub reports.</param>
/// <param name="Requested">The decision of the <see cref="IDiscoveryDecisionHandler"/>.</param>
/// <param name="Applied">
/// The decision carried out: <see cref="RenameDecision.KeepArchiveFolder"/> when the archive folder could not be
/// renamed.
/// </param>
/// <param name="CloneRelocation">
/// What happened to the clone of the repository; <see cref="CloneRelocationResult.TargetOccupied"/> means the user
/// must clear the folder of the new name.
/// </param>
/// <param name="WikiRelocation">What happened to the clone of the wiki.</param>
public sealed record RenameOutcome(
    string OldFullName,
    string NewFullName,
    RenameDecision Requested,
    RenameDecision Applied,
    CloneRelocationResult CloneRelocation,
    CloneRelocationResult WikiRelocation);
