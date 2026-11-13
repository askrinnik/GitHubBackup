using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Holds the summary of a finished run as the main window shows it: the same content as the summary of the console
/// application, built from <see cref="BackupRunSummary"/>.
/// </summary>
/// <remarks>
/// The summary never changes after it is created; a new run replaces the whole instance. Every text that comes from
/// the run passes through <see cref="ISecretMasker"/>, because an error message of a lower layer can carry a token.
/// </remarks>
public sealed class RunSummaryViewModel
{
    /// <summary>
    /// Initializes the summary.
    /// </summary>
    /// <param name="logPath">The path of the text log of the day.</param>
    private RunSummaryViewModel(string logPath) => LogPath = logPath;

    /// <summary>Gets the line that opens the summary, <see cref="BackupRunSummary.DryRunHeader"/> for a dry run, or <see langword="null"/>.</summary>
    public string? Header { get; private init; }

    /// <summary>Gets the errors that stopped the run; a summary with errors has no table and no totals.</summary>
    public IReadOnlyList<string> Errors { get; private init; } = [];

    /// <summary>Gets the warnings about <c>backup-config.json</c>, then the warning that the run is not in the run history.</summary>
    public IReadOnlyList<string> Warnings { get; private init; } = [];

    /// <summary>Gets the rows of the table, one per item, in the order the items were processed.</summary>
    public IReadOnlyList<RunSummaryRow> Rows { get; private init; } = [];

    /// <summary>Gets <see cref="BackupRunSummary.NoRepositoriesLine"/> when the run went through without any item, or <see langword="null"/>.</summary>
    public string? NoRepositoriesLine { get; private init; }

    /// <summary>Gets the lines of what the discovery of a dry run found.</summary>
    public IReadOnlyList<string> DiscoveryLines { get; private init; } = [];

    /// <summary>Gets <see cref="BackupRunSummary.CancelledLine"/> when the run was cancelled, or <see langword="null"/>.</summary>
    public string? CancelledLine { get; private init; }

    /// <summary>Gets the totals per status, or <see langword="null"/> when the run stopped or was cancelled before it started.</summary>
    public string? TotalsLine { get; private init; }

    /// <summary>Gets the path of the text log of the day.</summary>
    public string LogPath { get; }

    /// <summary>Gets whether the summary has errors.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Gets whether the summary has warnings.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Gets whether the summary has a table.</summary>
    public bool HasItems => Rows.Count > 0;

    /// <summary>Gets whether the summary lists what discovery found.</summary>
    public bool HasDiscoveryLines => DiscoveryLines.Count > 0;

    /// <summary>
    /// Creates the summary of a run that returned <paramref name="result"/>.
    /// </summary>
    /// <param name="result">The result of the run.</param>
    /// <param name="logPath">The path of the text log of the day.</param>
    /// <param name="secretMasker">Masks secrets in the texts of the run.</param>
    /// <returns>The summary.</returns>
    public static RunSummaryViewModel FromResult(BackupRunResult result, string logPath, ISecretMasker secretMasker)
    {
        var warnings = result.ConfigWarnings.Select(secretMasker.Mask).ToList();
        if (result.HistoryWarning is { } historyWarning)
        {
            warnings.Add(secretMasker.Mask(historyWarning));
        }

        if (result.CriticalErrors.Count > 0)
        {
            return new(logPath) { Errors = [.. result.CriticalErrors.Select(secretMasker.Mask)], Warnings = warnings };
        }

        return new(logPath)
        {
            Header = result.IsDryRun ? BackupRunSummary.DryRunHeader : null,
            Warnings = warnings,
            Rows = [.. result.Items.Select(item => CreateRow(item, secretMasker))],
            NoRepositoriesLine = BackupRunSummary.ShowsNoRepositoriesLine(result) ? BackupRunSummary.NoRepositoriesLine : null,
            DiscoveryLines = result.PlannedDiscovery is { } discovery ? [.. BackupRunSummary.DescribeDiscovery(discovery)] : [],
            CancelledLine = result.IsCancelled ? BackupRunSummary.CancelledLine : null,
            TotalsLine = BackupRunSummary.FormatTotals(result),
        };
    }

    /// <summary>
    /// Creates the summary of a run that was cancelled before the core took the run lock, so it has no result.
    /// </summary>
    /// <param name="logPath">The path of the text log of the day.</param>
    /// <returns>The summary with the cancellation line and the log path only.</returns>
    public static RunSummaryViewModel CancelledBeforeStart(string logPath) =>
        new(logPath) { CancelledLine = BackupRunSummary.CancelledLine };

    /// <summary>
    /// Creates the row of one item.
    /// </summary>
    /// <param name="item">The result of the item.</param>
    /// <param name="secretMasker">Masks secrets in the message of the item.</param>
    /// <returns>The row.</returns>
    private static RunSummaryRow CreateRow(BackupItemResult item, ISecretMasker secretMasker) => new(
        item.Repository,
        item.Status.ToString(),
        item.ArchiveFileName ?? string.Empty,
        BackupRunSummary.FormatDuration(item.Duration),
        item.Message is { } message ? secretMasker.Mask(message) : string.Empty);
}
