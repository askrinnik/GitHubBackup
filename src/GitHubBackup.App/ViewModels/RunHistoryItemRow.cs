namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One repository or wiki of a run in the run details, with every cell as display text.
/// </summary>
/// <param name="Repository">The repository as <c>owner/name</c>.</param>
/// <param name="Target">Whether the item is the repository or its wiki.</param>
/// <param name="Status">The name of the status of the item.</param>
/// <param name="Duration">The time spent on the item, as minutes, seconds and tenths.</param>
/// <param name="Branch">The branch that was backed up, or an empty text.</param>
/// <param name="HeadCommit">The HEAD commit that was backed up, or an empty text.</param>
/// <param name="Archive">The file name of the archive created in the run, or an empty text.</param>
/// <param name="ChangedRefs">The refs that changed since the previous archive, separated by commas.</param>
/// <param name="Error">The error, the warning or the reason for skipping, or an empty text.</param>
public sealed record RunHistoryItemRow(
    string Repository,
    string Target,
    string Status,
    string Duration,
    string Branch,
    string HeadCommit,
    string Archive,
    string ChangedRefs,
    string Error);
