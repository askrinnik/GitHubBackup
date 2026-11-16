namespace GitHubBackup.Core.History;

/// <summary>
/// The items of one repository in the most recent run that processed it.
/// </summary>
/// <param name="Repository">The item of the repository.</param>
/// <param name="Wiki">The item of its wiki from the same run, or <see langword="null"/> when the run has none.</param>
public sealed record LatestRepositoryRun(RunHistoryItem Repository, RunHistoryItem? Wiki);
