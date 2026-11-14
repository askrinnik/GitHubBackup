using System.Globalization;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One run of the run history with its details, every value as display text.
/// </summary>
public sealed record RunHistoryRow
{
    /// <summary>The format of the start and end times of a run.</summary>
    public const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The filter text of a run that covered every repository.</summary>
    public const string NoFilterText = "All repositories";

    /// <summary>The counts text of a run without items.</summary>
    public const string NoItemsText = "No items";

    /// <summary>Gets the identifier of the run.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the local time the run started, as recorded.</summary>
    public required string StartedAt { get; init; }

    /// <summary>Gets the local time the run finished, as recorded.</summary>
    public required string FinishedAt { get; init; }

    /// <summary>Gets the length of the run, as minutes, seconds and tenths.</summary>
    public required string Duration { get; init; }

    /// <summary>Gets how the run was started.</summary>
    public required string Mode { get; init; }

    /// <summary>Gets <c>Yes</c> for a dry run and <c>No</c> otherwise.</summary>
    public required string DryRun { get; init; }

    /// <summary>Gets the account or repository the run was limited to, as the command-line option.</summary>
    public required string Filter { get; init; }

    /// <summary>Gets the exit code of the run, as its name and number.</summary>
    public required string ExitCode { get; init; }

    /// <summary>Gets the number of items per status that occurs in the run.</summary>
    public required string Counts { get; init; }

    /// <summary>Gets the repositories and wikis of the run, in the order they were processed.</summary>
    public required IReadOnlyList<RunHistoryItemRow> Items { get; init; }

    /// <summary>Gets the discovery events of the run, in the order they were found.</summary>
    public required IReadOnlyList<DiscoveryEventRow> DiscoveryEvents { get; init; }

    /// <summary>Gets whether the run has discovery events.</summary>
    public bool HasDiscoveryEvents => DiscoveryEvents.Count > 0;

    /// <summary>
    /// Creates the row of a run.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="secretMasker">
    /// Masks secrets in the errors of the items, so the screen never shows a token whatever the database holds.
    /// </param>
    /// <returns>The row.</returns>
    public static RunHistoryRow Create(RunHistoryEntry run, ISecretMasker secretMasker) => new()
    {
        RunId = run.RunId,
        StartedAt = FormatTime(run.StartedAt),
        FinishedAt = FormatTime(run.FinishedAt),
        Duration = BackupRunSummary.FormatDuration(run.FinishedAt - run.StartedAt),
        Mode = run.Mode.ToString(),
        DryRun = run.IsDryRun ? "Yes" : "No",
        Filter = run switch
        {
            { AccountFilter: { } account } => $"--account {account}",
            { RepositoryFilter: { } repository } => $"--repo {repository}",
            _ => NoFilterText,
        },
        ExitCode = string.Create(CultureInfo.InvariantCulture, $"{run.ExitCode} ({(int)run.ExitCode})"),
        Counts = FormatCounts(run),
        Items = [.. run.Items.Select(item => CreateItemRow(item, secretMasker))],
        DiscoveryEvents = [.. run.DiscoveryEvents.Select(CreateEventRow)],
    };

    /// <summary>
    /// Formats a time as recorded, with the offset it was recorded with.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>The text in <see cref="DateFormat"/>.</returns>
    private static string FormatTime(DateTimeOffset time) => time.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns the number of items of each status that occurs in a run, in the order of the statuses.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>The counts, or <see cref="NoItemsText"/> when the run has no items.</returns>
    private static string FormatCounts(RunHistoryEntry run)
    {
        var counts = Enum.GetValues<BackupItemStatus>()
            .Select(status => (Status: status, Count: run.Count(status)))
            .Where(pair => pair.Count > 0)
            .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Status}: {pair.Count}"))
            .ToList();
        return counts.Count > 0 ? string.Join(", ", counts) : NoItemsText;
    }

    /// <summary>
    /// Creates the row of one item of a run.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="secretMasker">Masks secrets in the error of the item.</param>
    /// <returns>The row.</returns>
    private static RunHistoryItemRow CreateItemRow(RunHistoryItem item, ISecretMasker secretMasker) => new(
        item.Repository,
        item.Target.ToString(),
        item.Status.ToString(),
        BackupRunSummary.FormatDuration(item.Duration),
        item.Branch ?? string.Empty,
        item.HeadCommit ?? string.Empty,
        item.ArchiveFileName ?? string.Empty,
        string.Join(", ", item.ChangedRefs),
        item.Error is { } error ? secretMasker.Mask(error) : string.Empty);

    /// <summary>
    /// Creates the row of one discovery event of a run.
    /// </summary>
    /// <param name="discoveryEvent">The event.</param>
    /// <returns>The row.</returns>
    private static DiscoveryEventRow CreateEventRow(DiscoveryHistoryEvent discoveryEvent) => new(
        discoveryEvent.Kind.ToString(),
        discoveryEvent.Repository,
        discoveryEvent.PreviousFullName ?? string.Empty,
        discoveryEvent.Decision?.ToString() ?? string.Empty,
        discoveryEvent.DecidedBy?.ToString() ?? string.Empty);
}
