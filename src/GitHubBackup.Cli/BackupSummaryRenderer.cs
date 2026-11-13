using GitHubBackup.Core.Runs;
using Spectre.Console;

namespace GitHubBackup.Cli;

/// <summary>
/// Shows the result of a backup run: a table with one row per repository, the totals per status and the path of the
/// log, or, when the run was stopped, one line per error and the path of the log on the error output.
/// </summary>
/// <remarks>
/// A dry run adds a line that says nothing was changed before the table, lists what discovery found after it and
/// counts the statuses a dry run reports. A cancelled run adds a line that says so after the items and counts the
/// <see cref="BackupItemStatus.Cancelled"/> item too.
/// </remarks>
/// <param name="console">The outputs of the console application.</param>
internal sealed class BackupSummaryRenderer(CliConsole console)
{
    /// <summary>The prefix of each line that reports an error that stopped the run.</summary>
    public const string ErrorPrefix = "Error: ";

    /// <summary>
    /// The prefix of each line that reports a warning about <c>backup-config.json</c> or a run not recorded in the run
    /// history.
    /// </summary>
    public const string WarningPrefix = "Warning: ";

    /// <summary>The prefix of the line that names the log file.</summary>
    public const string LogPrefix = "Log: ";

    /// <summary>The line that opens the summary of a dry run.</summary>
    public const string DryRunHeader = BackupRunSummary.DryRunHeader;

    /// <summary>The line that follows the items of a cancelled run.</summary>
    public const string CancelledLine = BackupRunSummary.CancelledLine;

    /// <summary>
    /// Writes the summary of <paramref name="result"/>.
    /// </summary>
    /// <param name="result">The result of the run.</param>
    /// <param name="logPath">The path of the text log of the day.</param>
    /// <returns>A task that completes when the summary is written.</returns>
    public async Task RenderAsync(BackupRunResult result, string logPath)
    {
        var output = console.Output;
        foreach (var warning in result.ConfigWarnings)
        {
            output.MarkupLine($"[yellow]{Markup.Escape(WarningPrefix + warning)}[/]");
        }

        if (result.HistoryWarning is { } historyWarning)
        {
            output.MarkupLine($"[yellow]{Markup.Escape(WarningPrefix + historyWarning)}[/]");
        }

        if (result.CriticalErrors.Count > 0)
        {
            foreach (var error in result.CriticalErrors)
            {
                await console.Error.WriteLineAsync(ErrorPrefix + error);
            }

            await console.Error.WriteLineAsync(LogPrefix + logPath);
            return;
        }

        if (result.IsDryRun)
        {
            output.WriteLine(DryRunHeader);
        }

        if (result.Items.Count > 0)
        {
            output.Write(CreateTable(result.Items));
        }
        else if (BackupRunSummary.ShowsNoRepositoriesLine(result))
        {
            output.WriteLine(BackupRunSummary.NoRepositoriesLine);
        }

        if (result.PlannedDiscovery is { } discovery)
        {
            foreach (var line in BackupRunSummary.DescribeDiscovery(discovery))
            {
                output.WriteLine(line);
            }
        }

        if (result.IsCancelled)
        {
            output.MarkupLine($"[yellow]{Markup.Escape(CancelledLine)}[/]");
        }

        output.WriteLine(BackupRunSummary.FormatTotals(result));
        output.WriteLine(LogPrefix + logPath);
    }

    /// <summary>
    /// Builds the table of the repositories.
    /// </summary>
    /// <param name="items">The results of the repositories.</param>
    /// <returns>The table.</returns>
    private static Table CreateTable(IReadOnlyList<BackupItemResult> items)
    {
        var table = new Table().AddColumns("Repository", "Status", "Archive", "Duration", "Error");
        foreach (var item in items)
        {
            table.AddRow(
                new Text(item.Repository),
                new Text(item.Status.ToString(), GetStyle(item.Status)),
                new Text(item.ArchiveFileName ?? string.Empty),
                new Text(BackupRunSummary.FormatDuration(item.Duration)),
                new Text(item.Message ?? string.Empty));
        }

        return table;
    }

    /// <summary>
    /// Returns the colour of a status.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The style of the status cell.</returns>
    private static Style GetStyle(BackupItemStatus status) => status switch
    {
        BackupItemStatus.Archived => new(Color.Green),
        BackupItemStatus.Warning => new(Color.Yellow),
        BackupItemStatus.Failed => new(Color.Red),
        BackupItemStatus.Skipped => new(Color.Grey),
        BackupItemStatus.WouldArchive => new(Color.Green),
        BackupItemStatus.WouldClone => new(Color.Aqua),
        BackupItemStatus.Cancelled => new(Color.Yellow),
        _ => Style.Plain,
    };
}
