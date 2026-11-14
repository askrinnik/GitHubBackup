namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One discovery event of a run in the run details, with every cell as display text.
/// </summary>
/// <param name="Kind">What discovery found.</param>
/// <param name="Repository">The repository as <c>owner/name</c>; for a rename, the new name.</param>
/// <param name="PreviousName">The name before a rename, or an empty text.</param>
/// <param name="Decision">The decision taken on the event, or an empty text when none was asked for.</param>
/// <param name="DecidedBy">Who took the decision, or an empty text.</param>
public sealed record DiscoveryEventRow(
    string Kind, string Repository, string PreviousName, string Decision, string DecidedBy);
