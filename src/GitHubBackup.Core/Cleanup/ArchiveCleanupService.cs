using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Files;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// Removes the old archives of a repository by the rules of <see cref="ArchiveCleanupPlanner"/>.
/// </summary>
/// <param name="stateStore">Reads and replaces the state file.</param>
/// <param name="historyProvider">Reads which archive files are on the disk.</param>
/// <param name="recycleBin">Moves the archive files to the Recycle Bin.</param>
/// <param name="runLock">The lock a backup run holds, so the cleanup and a run never change the archives together.</param>
/// <param name="nameBuilder">Recognises the archive files of the repository by their names.</param>
/// <param name="logger">Logs the repository, the moved files, the removed records and the errors.</param>
public sealed class ArchiveCleanupService(
    IBackupStateStore stateStore,
    IArchiveHistoryProvider historyProvider,
    IRecycleBin recycleBin,
    IRunLock runLock,
    IArchiveNameBuilder nameBuilder,
    ILogger<ArchiveCleanupService> logger) : IArchiveCleanupService
{
    /// <inheritdoc />
    public async Task<ArchiveCleanupPlan> PrepareAsync(
        string archiveFolder, long repositoryId, string fullName, CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(archiveFolder, repositoryId, fullName, cancellationToken);
        var history = await historyProvider.LoadAsync(archiveFolder, repositoryId, fullName, cancellationToken);
        return ArchiveCleanupPlanner.Plan(state, history, nameBuilder, fullName);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cancellation stops before the next file; the state file is still written for the files already moved, and then
    /// the cancellation is reported.
    /// </remarks>
    public async Task<ArchiveCleanupResult> ExecuteAsync(
        string archiveFolder,
        long repositoryId,
        string fullName,
        ArchiveCleanupPlan confirmedPlan,
        CancellationToken cancellationToken)
    {
        using var handle = await runLock.TryAcquireAsync(cancellationToken);
        if (handle is null)
        {
            ArchiveCleanupLog.RunLockHeld(logger, fullName);
            return ArchiveCleanupResult.RunLockHeld;
        }

        var state = await stateStore.LoadAsync(archiveFolder, repositoryId, fullName, cancellationToken);
        var history = await historyProvider.LoadAsync(archiveFolder, repositoryId, fullName, cancellationToken);
        var plan = ArchiveCleanupPlanner.Plan(state, history, nameBuilder, fullName);
        if (!plan.Equals(confirmedPlan))
        {
            ArchiveCleanupLog.PlanChanged(logger, fullName);
            return ArchiveCleanupResult.PlanChanged;
        }

        ArchiveCleanupLog.Started(logger, fullName, plan.FileCount, plan.RecordCount);
        var errors = new List<string>();
        var (recycled, cancelled) = await RecycleFilesAsync(fullName, plan, errors, cancellationToken);

        List<ArchiveCleanupRecord> removed =
        [
            .. recycled.Where(file => file.IsInState).Select(file => new ArchiveCleanupRecord(file.Target, file.File)),
            .. plan.RecordsWithoutFiles,
        ];
        if (removed.Count > 0 && !await TryWriteStateAsync(archiveFolder, fullName, state, removed, errors))
        {
            removed = [];
        }

        if (cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return new(ArchiveCleanupStatus.Completed, recycled, removed, errors);
    }

    /// <summary>
    /// Moves the files of <paramref name="plan"/> to the Recycle Bin one by one, collecting the failures.
    /// </summary>
    /// <param name="fullName">The repository as <c>owner/name</c>, for the log.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="errors">Receives one sentence per file that stays.</param>
    /// <param name="cancellationToken">Stops before the next file.</param>
    /// <returns>The files moved, and whether the cancellation stopped the loop.</returns>
    private async Task<(List<ArchiveCleanupFile> Recycled, bool Cancelled)> RecycleFilesAsync(
        string fullName, ArchiveCleanupPlan plan, List<string> errors, CancellationToken cancellationToken)
    {
        var recycled = new List<ArchiveCleanupFile>();
        foreach (var file in plan.FilesToRecycle)
        {
            RecycleOutcome outcome;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                outcome = await recycleBin.RecycleAsync(file.FullPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The moved files still need their records removed, so the state file is written before the
                // cancellation reaches the caller.
                return (recycled, true);
            }
            catch (Exception exception)
            {
                // One unexpected failure keeps its file and record; the remaining files and the single state write
                // still happen.
                outcome = RecycleOutcome.Failed(exception.Message);
            }

            if (outcome.Succeeded)
            {
                ArchiveCleanupLog.FileRecycled(logger, fullName, file.Target, file.FullPath);
                recycled.Add(file);
            }
            else
            {
                var error = outcome.Error ?? "The file was not moved to the Recycle Bin.";
                ArchiveCleanupLog.FileFailed(logger, fullName, file.FullPath, error);
                errors.Add($"'{file.FullPath}': {error}");
            }
        }

        return (recycled, false);
    }

    /// <summary>
    /// Writes the state without the removed records once, atomically, and records a failure as an error.
    /// </summary>
    /// <remarks>
    /// The write is not cancellable: files already in the Recycle Bin are only consistent with the state file once their
    /// records are gone.
    /// </remarks>
    /// <param name="archiveFolder">The archive folder of the repository.</param>
    /// <param name="fullName">The repository as <c>owner/name</c>, for the log.</param>
    /// <param name="state">The state read under the lock.</param>
    /// <param name="removed">The records to remove.</param>
    /// <param name="errors">Receives the failure to write the state file.</param>
    /// <returns><see langword="true"/> when the state file holds the new state.</returns>
    private async Task<bool> TryWriteStateAsync(
        string archiveFolder,
        string fullName,
        BackupState state,
        IReadOnlyList<ArchiveCleanupRecord> removed,
        List<string> errors)
    {
        var newState = removed.GroupBy(record => record.Target).Aggregate(
            state, (current, group) => current.WithoutArchives(group.Key, group.Select(record => record.File)));
        try
        {
            await stateStore.ReplaceAsync(archiveFolder, newState, CancellationToken.None);
        }
        catch (Exception exception) when (exception is BackupStateException or IOException or UnauthorizedAccessException)
        {
            ArchiveCleanupLog.StateWriteFailed(logger, fullName, exception);
            errors.Add($"The backup state in '{archiveFolder}' cannot be written: {exception.Message} "
                + "The moved files stay in the Recycle Bin and show as missing; a new cleanup removes their records.");
            return false;
        }

        foreach (var record in removed)
        {
            ArchiveCleanupLog.RecordRemoved(logger, fullName, record.Target, record.File);
        }

        return true;
    }
}
