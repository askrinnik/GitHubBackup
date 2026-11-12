using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.History;

/// <summary>
/// Builds the <see cref="RunHistoryEntry"/> of a finished backup run from its request and its result.
/// </summary>
internal static class RunHistoryEntryBuilder
{
    /// <summary>The suffix that <see cref="BackupItemResult.Repository"/> adds to the name of a wiki.</summary>
    private const string _wikiSuffix = ".wiki";

    /// <summary>
    /// Builds the entry of a run.
    /// </summary>
    /// <remarks>
    /// The decisions of a backup run are taken by the automatic strategy in silent mode and by the user otherwise. A
    /// dry run decides nothing, so its events carry no decision.
    /// </remarks>
    /// <param name="runId">The identifier of the run.</param>
    /// <param name="startedAt">The local time the run started.</param>
    /// <param name="finishedAt">The local time the run finished.</param>
    /// <param name="request">The request the run was started with.</param>
    /// <param name="result">The result of the run.</param>
    /// <returns>The entry with the items and the discovery events of the run.</returns>
    public static RunHistoryEntry Build(
        Guid runId,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        BackupRunRequest request,
        BackupRunResult result) =>
        new(runId, startedAt, finishedAt, request.Mode, result.IsDryRun || request.DryRun, result.ExitCode)
        {
            AccountFilter = request.Filter.AccountLogin,
            RepositoryFilter = request.Filter.RepositoryFullName,
            Items = [.. result.Items.Select(CreateItem)],
            DiscoveryEvents =
            [
                .. CreateEvents(result.Discovery, GetDecisionMaker(request.Mode)),
                .. CreateEvents(result.PlannedDiscovery),
            ],
        };

    /// <summary>
    /// Converts the result of one repository or wiki to a history item.
    /// </summary>
    /// <param name="item">The result.</param>
    /// <returns>The item, named after the repository for a wiki too, since its target tells them apart.</returns>
    private static RunHistoryItem CreateItem(BackupItemResult item)
    {
        var repository = item.Target == BackupTarget.Wiki && item.Repository.EndsWith(_wikiSuffix, StringComparison.Ordinal)
            ? item.Repository[..^_wikiSuffix.Length]
            : item.Repository;
        return new(repository, item.Target, item.Status, item.Duration)
        {
            Branch = item.Branch,
            HeadCommit = item.HeadCommit,
            ArchiveFileName = item.ArchiveFileName,
            ChangedRefs = item.ChangedRefs,
            Error = item.Message,
        };
    }

    /// <summary>
    /// Returns who decided about the discovered repositories of a backup run in <paramref name="mode"/>.
    /// </summary>
    /// <param name="mode">How the run was started.</param>
    /// <returns><see cref="DecisionMaker.Automatic"/> in silent mode; <see cref="DecisionMaker.User"/> otherwise.</returns>
    private static DecisionMaker GetDecisionMaker(RunMode mode) =>
        mode == RunMode.Silent ? DecisionMaker.Automatic : DecisionMaker.User;

    /// <summary>
    /// Converts what the decisions of a backup run changed to discovery events.
    /// </summary>
    /// <param name="applied">What the decisions changed, or <see langword="null"/>.</param>
    /// <param name="decidedBy">Who took the decisions.</param>
    /// <returns>The new, missing, renamed and returned repositories, in that order.</returns>
    private static IEnumerable<DiscoveryHistoryEvent> CreateEvents(DiscoveryApplyResult? applied, DecisionMaker decidedBy)
    {
        if (applied is null)
        {
            return [];
        }

        return CreateNew(applied.Added, DiscoveryEventDecision.Added, decidedBy)
            .Concat(CreateNew(applied.Skipped, DiscoveryEventDecision.Skipped, decidedBy))
            .Concat(CreateNew(applied.Excluded, DiscoveryEventDecision.Excluded, decidedBy))
            .Concat(applied.MarkedUnavailable.Select(name => new DiscoveryHistoryEvent(name, DiscoveryEventKind.Missing)))
            .Concat(applied.Renamed.Select(rename => new DiscoveryHistoryEvent(rename.NewFullName, DiscoveryEventKind.Renamed)
            {
                PreviousFullName = rename.OldFullName,
                Decision = rename.Applied == RenameDecision.RenameArchiveFolder
                    ? DiscoveryEventDecision.FolderRenamed
                    : DiscoveryEventDecision.OldFolderKept,
                DecidedBy = decidedBy,
            }))
            .Concat(applied.Reactivated.Select(name => new DiscoveryHistoryEvent(name, DiscoveryEventKind.Returned)));
    }

    /// <summary>
    /// Creates the events of the new repositories that received one decision.
    /// </summary>
    /// <param name="names">The repositories as <c>owner/name</c>.</param>
    /// <param name="decision">The decision taken on them.</param>
    /// <param name="decidedBy">Who took it.</param>
    /// <returns>One event per repository.</returns>
    private static IEnumerable<DiscoveryHistoryEvent> CreateNew(
        IReadOnlyList<string> names, DiscoveryEventDecision decision, DecisionMaker decidedBy) =>
        names.Select(name => new DiscoveryHistoryEvent(name, DiscoveryEventKind.New)
        {
            Decision = decision,
            DecidedBy = decidedBy,
        });

    /// <summary>
    /// Converts what discovery found in a dry run to discovery events without decisions.
    /// </summary>
    /// <param name="planned">What discovery found, or <see langword="null"/>.</param>
    /// <returns>The new, missing, renamed and returned repositories, in that order.</returns>
    private static IEnumerable<DiscoveryHistoryEvent> CreateEvents(DiscoveryResult? planned)
    {
        if (planned is null)
        {
            return [];
        }

        return planned.NewInAccounts.Select(item => item.Repository.FullName)
            .Concat(planned.NewStandalone.Select(repository => repository.FullName))
            .Select(name => new DiscoveryHistoryEvent(name, DiscoveryEventKind.New))
            .Concat(planned.Missing.Select(tracked => new DiscoveryHistoryEvent(tracked.FullName, DiscoveryEventKind.Missing)))
            .Concat(planned.Renamed.Select(rename => new DiscoveryHistoryEvent(rename.Current.FullName, DiscoveryEventKind.Renamed)
            {
                PreviousFullName = rename.Tracked.FullName,
            }))
            .Concat(planned.Returned.Select(item => new DiscoveryHistoryEvent(item.Tracked.FullName, DiscoveryEventKind.Returned)));
    }
}
