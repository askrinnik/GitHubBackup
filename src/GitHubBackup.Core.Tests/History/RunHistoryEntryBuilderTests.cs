using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using NSubstitute;

namespace GitHubBackup.Core.Tests.History;

/// <summary>
/// Tests <see cref="RunHistoryEntryBuilder"/>.
/// </summary>
public sealed class RunHistoryEntryBuilderTests
{
    private static readonly Guid _runId = new("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    private static readonly DateTimeOffset _startedAt = new(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset _finishedAt = _startedAt.AddMinutes(3);

    private readonly IDiscoveryDecisionHandler _handler = Substitute.For<IDiscoveryDecisionHandler>();

    [Theory]
    [InlineData(RunMode.Interactive)]
    [InlineData(RunMode.Silent)]
    [InlineData(RunMode.Ui)]
    public void Build_Run_CopiesRunFields(RunMode mode)
    {
        var request = new BackupRunRequest(BackupRunFilter.All, _handler) { Mode = mode };

        var entry = Build(request, new([], [], []));

        entry.ShouldBe(
            new RunHistoryEntry(_runId, _startedAt, _finishedAt, mode, false, ExitCode.Success)
            {
                Items = entry.Items,
                DiscoveryEvents = entry.DiscoveryEvents,
            });
        entry.Items.ShouldBeEmpty();
        entry.DiscoveryEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Build_AccountFilter_RecordsAccount()
    {
        var entry = Build(new(BackupRunFilter.ForAccount("octo"), _handler), new([], [], []));

        entry.AccountFilter.ShouldBe("octo");
        entry.RepositoryFilter.ShouldBeNull();
    }

    [Fact]
    public void Build_RepositoryFilter_RecordsRepository()
    {
        var entry = Build(new(BackupRunFilter.ForRepository("octo", "one"), _handler), new([], [], []));

        entry.AccountFilter.ShouldBeNull();
        entry.RepositoryFilter.ShouldBe("octo/one");
    }

    [Fact]
    public void Build_Items_CopiesEveryFieldAndNamesWikiAfterRepository()
    {
        var result = new BackupRunResult(
            [
                new("octo/one", BackupItemStatus.Archived, "one.zip", TimeSpan.FromSeconds(2), null)
                {
                    Branch = "main",
                    HeadCommit = "abc",
                    ChangedRefs = ["heads/main", "tags/v1"],
                },
                new("octo/one.wiki", BackupItemStatus.Failed, null, TimeSpan.FromSeconds(1), "git failed.")
                {
                    Target = BackupTarget.Wiki,
                },
            ],
            [],
            []);

        var entry = Build(Request(), result);

        entry.Items.Count.ShouldBe(2);
        entry.Items[0].ShouldBeEquivalentTo(
            new RunHistoryItem("octo/one", BackupTarget.Repository, BackupItemStatus.Archived, TimeSpan.FromSeconds(2))
            {
                Branch = "main",
                HeadCommit = "abc",
                ArchiveFileName = "one.zip",
                ChangedRefs = ["heads/main", "tags/v1"],
            });
        entry.Items[1].ShouldBeEquivalentTo(
            new RunHistoryItem("octo/one", BackupTarget.Wiki, BackupItemStatus.Failed, TimeSpan.FromSeconds(1))
            {
                Error = "git failed.",
            });
        entry.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
        entry.Count(BackupItemStatus.Archived).ShouldBe(1);
    }

    [Theory]
    [InlineData(RunMode.Silent, DecisionMaker.Automatic)]
    [InlineData(RunMode.Interactive, DecisionMaker.User)]
    [InlineData(RunMode.Ui, DecisionMaker.User)]
    public void Build_AppliedDiscovery_RecordsEventsWithDecisionsInOrder(RunMode mode, DecisionMaker decidedBy)
    {
        var applied = new DiscoveryApplyResult(
            ["octo/added"],
            ["octo/skipped"],
            ["octo/excluded"],
            ["octo/missing"],
            ["octo/returned"],
            [
                Rename("octo/old", "octo/new", RenameDecision.RenameArchiveFolder),
                Rename("octo/kept", "octo/renamed", RenameDecision.KeepArchiveFolder),
            ],
            ConfigSaved: true);
        var request = Request() with { Mode = mode };

        var entry = Build(request, new([], [], []) { Discovery = applied });

        entry.DiscoveryEvents.ShouldBe(
        [
            new("octo/added", DiscoveryEventKind.New) { Decision = DiscoveryEventDecision.Added, DecidedBy = decidedBy },
            new("octo/skipped", DiscoveryEventKind.New) { Decision = DiscoveryEventDecision.Skipped, DecidedBy = decidedBy },
            new("octo/excluded", DiscoveryEventKind.New) { Decision = DiscoveryEventDecision.Excluded, DecidedBy = decidedBy },
            new("octo/missing", DiscoveryEventKind.Missing),
            new("octo/new", DiscoveryEventKind.Renamed)
            {
                PreviousFullName = "octo/old",
                Decision = DiscoveryEventDecision.FolderRenamed,
                DecidedBy = decidedBy,
            },
            new("octo/renamed", DiscoveryEventKind.Renamed)
            {
                PreviousFullName = "octo/kept",
                Decision = DiscoveryEventDecision.OldFolderKept,
                DecidedBy = decidedBy,
            },
            new("octo/returned", DiscoveryEventKind.Returned),
        ]);
    }

    [Fact]
    public void Build_DryRun_RecordsFlagAndEventsWithoutDecisions()
    {
        var planned = new DiscoveryResult(
            [new("octo", Repository(1, "octo", "new"))],
            [Repository(2, "other", "standalone")],
            [new("octo", "missing", 3, RepositoryStatus.Active, "octo")],
            [new(new("octo", "back", 4, RepositoryStatus.Unavailable, "octo"), Repository(4, "octo", "back"))],
            [new(new("octo", "old", 5, RepositoryStatus.Active, "octo"), Repository(5, "octo", "renamed"))]);
        var request = Request() with { DryRun = true, Mode = RunMode.Silent };

        var entry = Build(request, new([], [], []) { IsDryRun = true, PlannedDiscovery = planned });

        entry.IsDryRun.ShouldBeTrue();
        entry.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
        entry.DiscoveryEvents.ShouldBe(
        [
            new("octo/new", DiscoveryEventKind.New),
            new("other/standalone", DiscoveryEventKind.New),
            new("octo/missing", DiscoveryEventKind.Missing),
            new("octo/renamed", DiscoveryEventKind.Renamed) { PreviousFullName = "octo/old" },
            new("octo/back", DiscoveryEventKind.Returned),
        ]);
    }

    [Fact]
    public void Build_StoppedRun_RecordsCriticalWithoutItemsOrEvents()
    {
        var entry = Build(Request(), BackupRunResult.Stopped([BackupOrchestrator.RunLockHeldMessage], ["warning"]));

        entry.ExitCode.ShouldBe(ExitCode.Critical);
        entry.Items.ShouldBeEmpty();
        entry.DiscoveryEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Build_CancelledRun_RecordsCriticalWithItems()
    {
        var result = new BackupRunResult(
            [
                new("octo/one", BackupItemStatus.Cancelled, null, TimeSpan.FromSeconds(1), BackupOrchestrator.CancelledMessage),
                new("octo/two", BackupItemStatus.Skipped, null, TimeSpan.Zero, BackupOrchestrator.NotProcessedMessage),
            ],
            [],
            [])
        {
            IsCancelled = true,
        };

        var entry = Build(Request(), result);

        entry.ExitCode.ShouldBe(ExitCode.Critical);
        entry.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
        [
            ("octo/one", BackupItemStatus.Cancelled),
            ("octo/two", BackupItemStatus.Skipped),
        ]);
    }

    /// <summary>
    /// Builds the entry with the fixed run identifier and times.
    /// </summary>
    /// <param name="request">The request of the run.</param>
    /// <param name="result">The result of the run.</param>
    /// <returns>The entry.</returns>
    private static RunHistoryEntry Build(BackupRunRequest request, BackupRunResult result) =>
        RunHistoryEntryBuilder.Build(_runId, _startedAt, _finishedAt, request, result);

    /// <summary>
    /// Creates the request of an interactive run over every repository.
    /// </summary>
    /// <returns>The request.</returns>
    private BackupRunRequest Request() => new(BackupRunFilter.All, _handler);

    /// <summary>
    /// Creates the outcome of a rename whose clones were moved.
    /// </summary>
    /// <param name="oldFullName">The name before the rename.</param>
    /// <param name="newFullName">The name after the rename.</param>
    /// <param name="applied">The decision carried out.</param>
    /// <returns>The outcome.</returns>
    private static RenameOutcome Rename(string oldFullName, string newFullName, RenameDecision applied) =>
        new(oldFullName, newFullName, applied, applied, CloneRelocationResult.Moved, CloneRelocationResult.NotPresent);

    /// <summary>
    /// Creates a repository as GitHub reports it.
    /// </summary>
    /// <param name="id">The GitHub id.</param>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The repository.</returns>
    private static GitHubRepository Repository(long id, string owner, string name) =>
        new(id, owner, name, IsPrivate: false, IsFork: false, IsArchived: false, HasWiki: false, "main", $"https://github.com/{owner}/{name}");
}
