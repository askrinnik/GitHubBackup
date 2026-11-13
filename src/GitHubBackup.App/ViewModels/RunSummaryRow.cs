namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One row of the table of a run summary, with every cell as display text.
/// </summary>
/// <param name="Repository">The repository as <c>owner/name</c>, or its wiki as <c>owner/name.wiki</c>.</param>
/// <param name="Status">The name of the status of the item.</param>
/// <param name="Archive">The file name of the archive created in the run, or an empty text.</param>
/// <param name="Duration">The time spent on the item, as minutes, seconds and tenths.</param>
/// <param name="Error">The error, the warning or the reason for skipping, or an empty text.</param>
public sealed record RunSummaryRow(string Repository, string Status, string Archive, string Duration, string Error);
