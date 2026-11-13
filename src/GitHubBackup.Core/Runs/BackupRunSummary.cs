using System.Globalization;
using GitHubBackup.Core.Discovery;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Builds the content of the summary that both hosts show at the end of a run: the fixed lines, the statuses counted
/// in the totals, the lines of what a dry run's discovery found and the format of a duration.
/// </summary>
public static class BackupRunSummary
{
    /// <summary>The line that opens the summary of a dry run.</summary>
    public const string DryRunHeader = "Dry run: nothing was cloned, updated, archived or saved.";

    /// <summary>The line that opens the summary of a dry run that wrote the IDs of entries without one.</summary>
    public const string DryRunSavedIdsHeader =
        "Dry run: nothing was cloned, updated or archived; only the missing repository ids were saved to backup-config.json.";

    /// <summary>The line that follows the items of a cancelled run.</summary>
    public const string CancelledLine = "Cancelled: the run was stopped before it finished.";

    /// <summary>The line that replaces the table of a run that went through without any repository.</summary>
    public const string NoRepositoriesLine = "No repositories to back up.";

    /// <summary>The statuses counted in the totals of a normal run, in output order.</summary>
    private static readonly BackupItemStatus[] _runStatuses =
    [
        BackupItemStatus.Archived,
        BackupItemStatus.UpToDate,
        BackupItemStatus.Warning,
        BackupItemStatus.Failed,
        BackupItemStatus.Skipped,
    ];

    /// <summary>The statuses counted in the totals of a dry run, in output order.</summary>
    private static readonly BackupItemStatus[] _dryRunStatuses =
    [
        BackupItemStatus.WouldClone,
        BackupItemStatus.WouldArchive,
        BackupItemStatus.UpToDate,
        BackupItemStatus.Warning,
        BackupItemStatus.Failed,
        BackupItemStatus.Skipped,
    ];

    /// <summary>
    /// Returns the line that opens the summary of a dry run.
    /// </summary>
    /// <param name="result">The result of a dry run.</param>
    /// <returns>
    /// <see cref="DryRunSavedIdsHeader"/> when the run wrote IDs to <c>backup-config.json</c>, otherwise
    /// <see cref="DryRunHeader"/>.
    /// </returns>
    public static string GetDryRunHeader(BackupRunResult result) =>
        result.SavedRepositoryIds.Count > 0 ? DryRunSavedIdsHeader : DryRunHeader;

    /// <summary>
    /// Returns the statuses the totals of <paramref name="result"/> count, in output order.
    /// </summary>
    /// <remarks>
    /// A cancelled run adds <see cref="BackupItemStatus.Cancelled"/> at the end; a run that went through has no item
    /// with that status, so its totals leave it out.
    /// </remarks>
    /// <param name="result">The result of the run.</param>
    /// <returns>The statuses.</returns>
    public static IReadOnlyList<BackupItemStatus> GetTotalStatuses(BackupRunResult result)
    {
        var listed = result.IsDryRun ? _dryRunStatuses : _runStatuses;
        return result.IsCancelled ? [.. listed, BackupItemStatus.Cancelled] : listed;
    }

    /// <summary>
    /// Formats the totals of <paramref name="result"/>, such as <c>Archived: 1, UpToDate: 2, …</c>.
    /// </summary>
    /// <param name="result">The result of the run.</param>
    /// <returns>The totals line.</returns>
    public static string FormatTotals(BackupRunResult result) => string.Join(
        ", ",
        GetTotalStatuses(result).Select(status =>
            string.Create(CultureInfo.InvariantCulture, $"{status}: {result.Count(status)}")));

    /// <summary>
    /// Returns whether the summary of <paramref name="result"/> shows <see cref="NoRepositoriesLine"/>: the run went
    /// through, was not cancelled and has no item.
    /// </summary>
    /// <param name="result">The result of the run.</param>
    /// <returns><see langword="true"/> when the line is shown.</returns>
    public static bool ShowsNoRepositoriesLine(BackupRunResult result) =>
        result.CriticalErrors.Count == 0 && result.Items.Count == 0 && !result.IsCancelled;

    /// <summary>
    /// Lists what discovery found in a dry run, one repository per line.
    /// </summary>
    /// <param name="discovery">What discovery found.</param>
    /// <returns>The new, missing, renamed and returned repositories, in that order.</returns>
    public static IEnumerable<string> DescribeDiscovery(DiscoveryResult discovery) =>
        discovery.NewInAccounts.Select(item => $"New: {item.Repository.FullName}")
            .Concat(discovery.NewStandalone.Select(repository => $"New: {repository.FullName}"))
            .Concat(discovery.Missing.Select(repository => $"Missing: {repository.FullName}"))
            .Concat(discovery.Renamed.Select(item => $"Renamed: {item.Tracked.FullName} -> {item.Current.FullName}"))
            .Concat(discovery.Returned.Select(item => $"Returned: {item.Tracked.FullName}"));

    /// <summary>
    /// Formats a duration as minutes, seconds and tenths, such as <c>2:05.3</c>.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The text, independent of the culture.</returns>
    public static string FormatDuration(TimeSpan duration) => string.Create(
        CultureInfo.InvariantCulture,
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}.{duration.Milliseconds / 100}");
}
