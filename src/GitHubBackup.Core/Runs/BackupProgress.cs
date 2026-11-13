namespace GitHubBackup.Core.Runs;

/// <summary>
/// Describes what a backup run is doing at the moment: the operation and the repository or wiki it works on.
/// </summary>
/// <param name="Stage">The operation.</param>
/// <param name="Repository">
/// The repository as <c>owner/name</c> or its wiki as <c>owner/name.wiki</c>, or <see langword="null"/> when the
/// operation concerns no single repository, as for <see cref="BackupProgressStage.Discovering"/>.
/// </param>
/// <param name="Index">
/// The 1-based position of the repository among the repositories of the run, which its wiki shares, or <c>0</c> without
/// a repository.
/// </param>
/// <param name="Total">The number of repositories of the run, or <c>0</c> without a repository.</param>
public sealed record BackupProgress(BackupProgressStage Stage, string? Repository, int Index, int Total);
