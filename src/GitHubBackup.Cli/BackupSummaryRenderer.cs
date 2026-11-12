using System.Globalization;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Runs;
using Spectre.Console;

namespace GitHubBackup.Cli;

/// <summary>
/// Shows the result of a backup run: a table with one row per repository, the totals per status and the path of the
/// log, or, when the run was stopped, one line per error and the path of the log on the error output.
/// </summary>
/// <remarks>
/// A dry run adds a line that says nothing was changed before the table, lists what discovery found after it and
/// counts the statuses a dry run reports.
/// </remarks>
/// <param name="console">The outputs of the console application.</param>
internal sealed class BackupSummaryRenderer(CliConsole console)
{
    /// <summary>The prefix of each line that reports an error that stopped the run.</summary>
    public const string ErrorPrefix = "Error: ";

    /// <summary>The prefix of each line that reports a warning about <c>backup-config.json</c>.</summary>
    public const string WarningPrefix = "Warning: ";

    /// <summary>The prefix of the line that names the log file.</summary>
    public const string LogPrefix = "Log: ";

    /// <summary>The line that opens the summary of a dry run.</summary>
    public const string DryRunHeader = "Dry run: nothing was cloned, updated, archived or saved.";

    /// <summary>The statuses listed in the totals line of a normal run, in output order.</summary>
    private static readonly BackupItemStatus[] _runStatuses =
    [
        BackupItemStatus.Archived,
        BackupItemStatus.UpToDate,
        BackupItemStatus.Warning,
        BackupItemStatus.Failed,
        BackupItemStatus.Skipped,
    ];

    /// <summary>The statuses listed in the totals line of a dry run, in output order.</summary>
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

        if (result.Items.Count == 0)
        {
            output.WriteLine("No repositories to back up.");
        }
        else
        {
            output.Write(CreateTable(result.Items));
        }

        if (result.PlannedDiscovery is { } discovery)
        {
            foreach (var line in DescribeDiscovery(discovery))
            {
                output.WriteLine(line);
            }
        }

        output.WriteLine(string.Join(
            ", ",
            (result.IsDryRun ? _dryRunStatuses : _runStatuses).Select(status => $"{status}: {result.Count(status)}")));
        output.WriteLine(LogPrefix + logPath);
    }

    /// <summary>
    /// Lists what discovery found in a dry run, one repository per line.
    /// </summary>
    /// <param name="discovery">What discovery found.</param>
    /// <returns>The new, missing, renamed and returned repositories, in that order.</returns>
    private static IEnumerable<string> DescribeDiscovery(DiscoveryResult discovery) =>
        discovery.NewInAccounts.Select(item => $"New: {item.Repository.FullName}")
            .Concat(discovery.NewStandalone.Select(repository => $"New: {repository.FullName}"))
            .Concat(discovery.Missing.Select(repository => $"Missing: {repository.FullName}"))
            .Concat(discovery.Renamed.Select(item => $"Renamed: {item.Tracked.FullName} -> {item.Current.FullName}"))
            .Concat(discovery.Returned.Select(item => $"Returned: {item.Tracked.FullName}"));

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
                new Text(FormatDuration(item.Duration)),
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
        _ => Style.Plain,
    };

    /// <summary>
    /// Formats a duration as minutes, seconds and tenths, such as <c>2:05.3</c>.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The text, independent of the culture.</returns>
    private static string FormatDuration(TimeSpan duration) => string.Create(
        CultureInfo.InvariantCulture,
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}.{duration.Milliseconds / 100}");
}
