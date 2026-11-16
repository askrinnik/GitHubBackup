using GitHubBackup.Core.Runs;

namespace GitHubBackup.Core.History;

/// <summary>
/// Derives the <see cref="WikiStatus"/> of a repository from its latest run.
/// </summary>
public static class WikiStatusResolver
{
    /// <summary>
    /// Resolves the wiki status from the wiki item of the run or, when the run has none, from the repository item.
    /// </summary>
    /// <param name="run">The latest run of the repository, or <see langword="null"/> when none recorded it.</param>
    /// <returns>The status.</returns>
    public static WikiStatus Resolve(LatestRepositoryRun? run)
    {
        if (run is null)
        {
            return WikiStatus.Unknown;
        }

        if (run.Wiki is { } wiki)
        {
            return wiki.Status switch
            {
                BackupItemStatus.Archived or BackupItemStatus.UpToDate or BackupItemStatus.Warning
                    or BackupItemStatus.WouldArchive or BackupItemStatus.WouldClone => WikiStatus.Ok,
                BackupItemStatus.Skipped => WikiStatus.NoPages,
                BackupItemStatus.Failed => WikiStatus.Failed,
                BackupItemStatus.Cancelled => WikiStatus.Cancelled,
                _ => WikiStatus.Unknown,
            };
        }

        return run.Repository.Status switch
        {
            BackupItemStatus.Archived or BackupItemStatus.UpToDate or BackupItemStatus.Warning
                or BackupItemStatus.WouldArchive or BackupItemStatus.WouldClone => WikiStatus.Off,
            _ => WikiStatus.Unknown,
        };
    }
}
