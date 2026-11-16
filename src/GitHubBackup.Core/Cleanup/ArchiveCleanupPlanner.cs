using GitHubBackup.Core.History;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// Decides which archives of a repository and its wiki the cleanup of old archives removes.
/// </summary>
/// <remarks>
/// Each target keeps its last record, the one the next run compares the server with. When the file of that record is
/// gone, the newest record whose file is on the disk is kept too, so the target keeps a restorable archive. Every other
/// record goes: with its file to the Recycle Bin, or alone when its file is gone. A file without a record goes to the
/// Recycle Bin only when the target keeps a file on the disk, so the cleanup never leaves a target without an archive.
/// </remarks>
public static class ArchiveCleanupPlanner
{
    /// <summary>
    /// Builds the cleanup plan of a repository.
    /// </summary>
    /// <param name="state">The state file of the repository, whose record order decides which record is the last.</param>
    /// <param name="history">
    /// The archive history read from <paramref name="state"/> and the archive folder: which recorded files are on the
    /// disk, their sizes, and the archive files without a record.
    /// </param>
    /// <returns>The plan; <see cref="ArchiveCleanupPlan.IsEmpty"/> when nothing is to be removed.</returns>
    public static ArchiveCleanupPlan Plan(BackupState state, ArchiveHistory history)
    {
        var files = new List<ArchiveCleanupFile>();
        var records = new List<ArchiveCleanupRecord>();
        foreach (var target in (BackupTarget[])[BackupTarget.Repository, BackupTarget.Wiki])
        {
            PlanTarget(target, state.GetSection(target), history, files, records);
        }

        return files.Count == 0 && records.Count == 0 ? ArchiveCleanupPlan.Empty : new(files, records);
    }

    /// <summary>
    /// Adds what the cleanup removes of one target to <paramref name="files"/> and <paramref name="records"/>.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="section">The records of the target, oldest first.</param>
    /// <param name="history">The archive history of the repository.</param>
    /// <param name="files">Receives the files to move to the Recycle Bin.</param>
    /// <param name="records">Receives the records without a file to remove.</param>
    private static void PlanTarget(
        BackupTarget target,
        BackupStateSection section,
        ArchiveHistory history,
        List<ArchiveCleanupFile> files,
        List<ArchiveCleanupRecord> records)
    {
        if (section.LastArchive is not { } last)
        {
            // Without a record the target keeps no archive, so its files without a record are left alone.
            return;
        }

        // Windows compares file names without regard to case, so a record matches its file in any case.
        var onDisk = history.Archives
            .Where(entry => entry.Target == target && entry.IsInState && entry.FileExists)
            .GroupBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var kept = new List<ArchiveRecord> { last };
        if (!onDisk.ContainsKey(last.File)
            && section.Archives.LastOrDefault(record => onDisk.ContainsKey(record.File)) is { } newestOnDisk)
        {
            kept.Add(newestOnDisk);
        }

        var keptFiles = kept.Select(record => record.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var record in section.Archives.Where(record => !keptFiles.Contains(record.File)))
        {
            if (onDisk.TryGetValue(record.File, out var entry))
            {
                files.Add(new(target, record.File, entry.FullPath, entry.DiskSizeBytes ?? 0, IsInState: true));
            }
            else
            {
                records.Add(new(target, record.File));
            }
        }

        if (!kept.Any(record => onDisk.ContainsKey(record.File)))
        {
            return;
        }

        files.AddRange(history.Archives
            .Where(entry => entry.Target == target && !entry.IsInState && entry.FileExists)
            .OrderBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ArchiveCleanupFile(
                target, entry.File, entry.FullPath, entry.DiskSizeBytes ?? 0, IsInState: false)));
    }
}
