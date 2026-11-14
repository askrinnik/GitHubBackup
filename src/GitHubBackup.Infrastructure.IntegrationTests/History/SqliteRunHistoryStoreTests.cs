using GitHubBackup.Core;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.State;
using GitHubBackup.Infrastructure.History;

namespace GitHubBackup.Infrastructure.IntegrationTests.History;

/// <summary>
/// Tests <see cref="SqliteRunHistoryStore"/> against a real SQLite database file in a temp folder.
/// </summary>
public sealed class SqliteRunHistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset _startedAt = new(2026, 11, 12, 10, 15, 30, 123, TimeSpan.FromHours(3));

    private readonly TemporaryHistoryDatabase _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>Gets every item status.</summary>
    public static TheoryData<BackupItemStatus> Statuses => [.. Enum.GetValues<BackupItemStatus>()];

    [Fact]
    public async Task AddAsync_RunWithItemsAndDiscoveryEvents_GetAsyncReturnsEqualRun()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var run = CreateRun() with
        {
            Items =
            [
                new("octo/one", BackupTarget.Repository, BackupItemStatus.Archived, TimeSpan.FromSeconds(12.5))
                {
                    Branch = "main",
                    HeadCommit = "0123456789abcdef0123456789abcdef01234567",
                    ArchiveFileName = "one_2026-11-12_10-15_0123456789.zip",
                    ChangedRefs = ["refs/heads/main", "refs/tags/v1.0"],
                },
                new("octo/one", BackupTarget.Wiki, BackupItemStatus.UpToDate, TimeSpan.FromSeconds(1))
                {
                    Branch = "master",
                    HeadCommit = "89abcdef0123456789abcdef0123456789abcdef",
                },
                new("octo/two", BackupTarget.Repository, BackupItemStatus.Failed, TimeSpan.FromMilliseconds(250))
                {
                    Error = "git fetch exited with code 128.",
                },
            ],
            DiscoveryEvents =
            [
                new("octo/new", DiscoveryEventKind.New) { Decision = DiscoveryEventDecision.Added, DecidedBy = DecisionMaker.User },
                new("octo/renamed", DiscoveryEventKind.Renamed)
                {
                    PreviousFullName = "octo/old-name",
                    Decision = DiscoveryEventDecision.FolderRenamed,
                    DecidedBy = DecisionMaker.Automatic,
                },
                new("octo/gone", DiscoveryEventKind.Missing),
            ],
        };

        await store.AddAsync(run, CancellationToken);
        var loaded = await store.GetAsync(run.RunId, CancellationToken);

        // Records compare their lists by reference, so the lists are compared element by element.
        loaded.ShouldNotBeNull();
        (loaded with { Items = run.Items, DiscoveryEvents = run.DiscoveryEvents }).ShouldBe(run);
        IReadOnlyList<string> noRefs = [];
        loaded.Items.Select(item => item with { ChangedRefs = noRefs })
            .ShouldBe(run.Items.Select(item => item with { ChangedRefs = noRefs }));
        loaded.Items.Select(item => string.Join(",", item.ChangedRefs))
            .ShouldBe(["refs/heads/main,refs/tags/v1.0", "", ""]);
        loaded.DiscoveryEvents.ShouldBe(run.DiscoveryEvents);
        loaded.StartedAt.Offset.ShouldBe(TimeSpan.FromHours(3));
        loaded.StartedAt.ShouldBe(run.StartedAt);
    }

    [Fact]
    public async Task AddAsync_OptionalFieldsNull_ReadsBackNull()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var run = CreateRun() with
        {
            Items = [new("octo/one", BackupTarget.Repository, BackupItemStatus.Skipped, TimeSpan.Zero)],
            DiscoveryEvents = [new("octo/back", DiscoveryEventKind.Returned)],
        };

        await store.AddAsync(run, CancellationToken);
        var loaded = (await store.GetAsync(run.RunId, CancellationToken)).ShouldNotBeNull();

        loaded.AccountFilter.ShouldBeNull();
        loaded.RepositoryFilter.ShouldBeNull();
        var item = loaded.Items.ShouldHaveSingleItem();
        item.Branch.ShouldBeNull();
        item.HeadCommit.ShouldBeNull();
        item.ArchiveFileName.ShouldBeNull();
        item.Error.ShouldBeNull();
        item.ChangedRefs.ShouldBeEmpty();
        var discoveryEvent = loaded.DiscoveryEvents.ShouldHaveSingleItem();
        discoveryEvent.PreviousFullName.ShouldBeNull();
        discoveryEvent.Decision.ShouldBeNull();
        discoveryEvent.DecidedBy.ShouldBeNull();
    }

    [Theory]
    [InlineData("octo", null)]
    [InlineData(null, "octo/one")]
    public async Task AddAsync_Filter_ReadsBackFilter(string? account, string? repository)
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var run = CreateRun() with { AccountFilter = account, RepositoryFilter = repository };

        await store.AddAsync(run, CancellationToken);
        var loaded = (await store.GetAsync(run.RunId, CancellationToken)).ShouldNotBeNull();

        loaded.AccountFilter.ShouldBe(account);
        loaded.RepositoryFilter.ShouldBe(repository);
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task AddAsync_EachStatus_StoredAsName(BackupItemStatus status)
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var run = CreateRun() with { Items = [new("octo/one", BackupTarget.Repository, status, TimeSpan.Zero)] };

        await store.AddAsync(run, CancellationToken);

        (await _database.QueryScalarAsync("SELECT Status FROM RunItems", CancellationToken)).ShouldBe(status.ToString());
        (await store.GetAsync(run.RunId, CancellationToken)).ShouldNotBeNull().Items.ShouldHaveSingleItem().Status.ShouldBe(status);
    }

    [Fact]
    public async Task AddAsync_OtherEnums_StoredAsNames()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var run = CreateRun() with
        {
            Items = [new("octo/one", BackupTarget.Wiki, BackupItemStatus.Archived, TimeSpan.Zero)],
            DiscoveryEvents =
            [
                new("octo/new", DiscoveryEventKind.New) { Decision = DiscoveryEventDecision.Excluded, DecidedBy = DecisionMaker.User },
            ],
        };

        await store.AddAsync(run, CancellationToken);

        (await _database.QueryScalarAsync("SELECT Mode FROM Runs", CancellationToken)).ShouldBe("Interactive");
        (await _database.QueryScalarAsync("SELECT Target FROM RunItems", CancellationToken)).ShouldBe("Wiki");
        (await _database.QueryScalarAsync("SELECT Kind FROM DiscoveryEvents", CancellationToken)).ShouldBe("New");
        (await _database.QueryScalarAsync("SELECT Decision FROM DiscoveryEvents", CancellationToken)).ShouldBe("Excluded");
        (await _database.QueryScalarAsync("SELECT DecidedBy FROM DiscoveryEvents", CancellationToken)).ShouldBe("User");
    }

    [Fact]
    public async Task AddAsync_ErrorWithRegisteredSecret_StoresMaskedError()
    {
        var masker = new SecretMasker();
        masker.Register("ghp_secretvalue");
        var store = await _database.CreateMigratedStoreAsync(CancellationToken, masker);
        var run = CreateRun() with
        {
            Items =
            [
                new("octo/one", BackupTarget.Repository, BackupItemStatus.Failed, TimeSpan.Zero)
                {
                    Error = "fatal: Authorization: token ghp_secretvalue rejected",
                },
            ],
        };

        await store.AddAsync(run, CancellationToken);

        (await _database.QueryScalarAsync("SELECT Error FROM RunItems", CancellationToken))
            .ShouldBe("fatal: Authorization: token *** rejected");
    }

    [Fact]
    public async Task AddAsync_ExitCode_StoredAsNumber()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var run = CreateRun() with { ExitCode = ExitCode.DiscoveryEvents };

        await store.AddAsync(run, CancellationToken);

        (await _database.QueryScalarAsync("SELECT typeof(ExitCode) FROM Runs", CancellationToken)).ShouldBe("integer");
        (await _database.QueryScalarAsync("SELECT ExitCode FROM Runs", CancellationToken)).ShouldBe(3L);
        (await store.GetAsync(run.RunId, CancellationToken)).ShouldNotBeNull().ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
    }

    [Fact]
    public async Task AddAsync_DryRun_ReadsBackFlag()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var run = CreateRun() with { Mode = RunMode.Silent, IsDryRun = true };

        await store.AddAsync(run, CancellationToken);
        var loaded = (await store.GetAsync(run.RunId, CancellationToken)).ShouldNotBeNull();

        loaded.IsDryRun.ShouldBeTrue();
        loaded.Mode.ShouldBe(RunMode.Silent);
    }

    [Fact]
    public async Task GetAsync_UnknownRunId_ReturnsNull()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        await store.AddAsync(CreateRun(), CancellationToken);

        var loaded = await store.GetAsync(Guid.NewGuid(), CancellationToken);

        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task AddAsync_SameRunIdTwice_ThrowsRunHistoryException()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var run = CreateRun();
        await store.AddAsync(run, CancellationToken);

        var exception = await Should.ThrowAsync<RunHistoryException>(() => store.AddAsync(run, CancellationToken));

        exception.DatabasePath.ShouldBe(_database.DatabasePath);
        exception.Message.ShouldContain(_database.DatabasePath);
        exception.Message.ShouldContain(run.RunId.ToString());
        (await _database.QueryScalarAsync("SELECT COUNT(*) FROM Runs", CancellationToken)).ShouldBe(1L);
    }

    [Fact]
    public async Task AddAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exception = await Should.ThrowAsync<Exception>(() => store.AddAsync(CreateRun(), cancellation.Token));

        exception.ShouldBeAssignableTo<OperationCanceledException>();
        (await _database.QueryScalarAsync("SELECT COUNT(*) FROM Runs", CancellationToken)).ShouldBe(0L);
    }

    [Fact]
    public async Task QueryAsync_EmptyHistory_ReturnsEmpty()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);

        var runs = await store.QueryAsync(RunHistoryQuery.All, CancellationToken);

        runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task QueryAsync_NoFilter_ReturnsEveryRunNewestFirstWithItemsAndEvents()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var first = CreateRun() with
        {
            Items =
            [
                new("octo/one", BackupTarget.Repository, BackupItemStatus.Archived, TimeSpan.FromSeconds(3)),
                new("octo/two", BackupTarget.Wiki, BackupItemStatus.UpToDate, TimeSpan.FromSeconds(1)),
            ],
            DiscoveryEvents = [new("octo/new", DiscoveryEventKind.New) { Decision = DiscoveryEventDecision.Added, DecidedBy = DecisionMaker.User }],
        };
        var second = CreateRun(_startedAt.AddHours(1));
        await store.AddAsync(first, CancellationToken);
        await store.AddAsync(second, CancellationToken);

        var runs = await store.QueryAsync(RunHistoryQuery.All, CancellationToken);

        runs.Select(run => run.RunId).ShouldBe([second.RunId, first.RunId]);
        runs[1].Items.Select(item => item.Repository).ShouldBe(["octo/one", "octo/two"]);
        runs[1].DiscoveryEvents.ShouldBe(first.DiscoveryEvents);
        runs[0].Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(RunMode.Interactive)]
    [InlineData(RunMode.Silent)]
    [InlineData(RunMode.Ui)]
    public async Task QueryAsync_Mode_ReturnsRunsOfMode(RunMode mode)
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var runs = Enum.GetValues<RunMode>().Select(value => CreateRun() with { Mode = value }).ToList();
        foreach (var run in runs)
        {
            await store.AddAsync(run, CancellationToken);
        }

        var selected = await store.QueryAsync(new() { Mode = mode }, CancellationToken);

        selected.ShouldHaveSingleItem().RunId.ShouldBe(runs.Single(run => run.Mode == mode).RunId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task QueryAsync_DryRun_ReturnsRunsWithFlag(bool isDryRun)
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var dryRun = CreateRun() with { IsDryRun = true };
        var regular = CreateRun();
        await store.AddAsync(dryRun, CancellationToken);
        await store.AddAsync(regular, CancellationToken);

        var selected = await store.QueryAsync(new() { IsDryRun = isDryRun }, CancellationToken);

        selected.ShouldHaveSingleItem().RunId.ShouldBe(isDryRun ? dryRun.RunId : regular.RunId);
    }

    [Theory]
    [InlineData(ExitCode.Success)]
    [InlineData(ExitCode.RepositoriesFailed)]
    [InlineData(ExitCode.Critical)]
    [InlineData(ExitCode.DiscoveryEvents)]
    public async Task QueryAsync_ExitCode_ReturnsRunsWithExitCode(ExitCode exitCode)
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var runs = Enum.GetValues<ExitCode>().Select(value => CreateRun() with { ExitCode = value }).ToList();
        foreach (var run in runs)
        {
            await store.AddAsync(run, CancellationToken);
        }

        var selected = await store.QueryAsync(new() { ExitCode = exitCode }, CancellationToken);

        selected.ShouldHaveSingleItem().RunId.ShouldBe(runs.Single(run => run.ExitCode == exitCode).RunId);
    }

    [Fact]
    public async Task QueryAsync_Period_IncludesFromAndExcludesTo()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var before = CreateRun(_startedAt.AddTicks(-1));
        var atFrom = CreateRun(_startedAt);
        var beforeTo = CreateRun(_startedAt.AddHours(1).AddTicks(-1));
        var atTo = CreateRun(_startedAt.AddHours(1));
        foreach (var run in new[] { before, atFrom, beforeTo, atTo })
        {
            await store.AddAsync(run, CancellationToken);
        }

        var selected = await store.QueryAsync(new() { From = _startedAt, To = _startedAt.AddHours(1) }, CancellationToken);

        selected.Select(run => run.RunId).ShouldBe([beforeTo.RunId, atFrom.RunId]);
    }

    [Fact]
    public async Task QueryAsync_DifferentOffsets_ComparesInstants()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);

        // 08:00 at +00:00 is 11:00 at +03:00, after the bound, although its text sorts before it.
        var utcRun = CreateRun(new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero));
        var localRun = CreateRun(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(3)));
        await store.AddAsync(utcRun, CancellationToken);
        await store.AddAsync(localRun, CancellationToken);

        var selected = await store.QueryAsync(
            new() { From = new DateTimeOffset(2026, 11, 12, 10, 30, 0, TimeSpan.FromHours(3)) }, CancellationToken);

        selected.ShouldHaveSingleItem().RunId.ShouldBe(utcRun.RunId);
    }

    [Fact]
    public async Task QueryAsync_Combination_AppliesEveryFilter()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var match = CreateRun() with { Mode = RunMode.Silent, ExitCode = ExitCode.RepositoriesFailed };
        var otherMode = CreateRun() with { Mode = RunMode.Ui, ExitCode = ExitCode.RepositoriesFailed };
        var otherCode = CreateRun() with { Mode = RunMode.Silent };
        var dryRun = CreateRun() with { Mode = RunMode.Silent, ExitCode = ExitCode.RepositoriesFailed, IsDryRun = true };
        var early = CreateRun(_startedAt.AddDays(-1)) with { Mode = RunMode.Silent, ExitCode = ExitCode.RepositoriesFailed };
        foreach (var run in new[] { match, otherMode, otherCode, dryRun, early })
        {
            await store.AddAsync(run, CancellationToken);
        }

        var selected = await store.QueryAsync(
            new()
            {
                From = _startedAt.AddHours(-1),
                To = _startedAt.AddHours(1),
                Mode = RunMode.Silent,
                IsDryRun = false,
                ExitCode = ExitCode.RepositoriesFailed,
            },
            CancellationToken);

        selected.ShouldHaveSingleItem().RunId.ShouldBe(match.RunId);
    }

    [Fact]
    public async Task QueryAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exception = await Should.ThrowAsync<Exception>(() => store.QueryAsync(RunHistoryQuery.All, cancellation.Token));

        exception.ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task QueryAsync_DatabaseWithoutSchema_ThrowsRunHistoryExceptionWithPath()
    {
        var store = _database.CreateStoreWithoutSchema();

        var exception = await Should.ThrowAsync<RunHistoryException>(
            () => store.QueryAsync(RunHistoryQuery.All, CancellationToken));

        exception.DatabasePath.ShouldBe(_database.DatabasePath);
        exception.Message.ShouldContain(_database.DatabasePath);
    }

    /// <summary>
    /// Deletes the temp folder with the database.
    /// </summary>
    public void Dispose() => _database.Dispose();

    /// <summary>
    /// Creates a run with a new identifier, no filter, no items and no discovery events.
    /// </summary>
    /// <returns>The run.</returns>
    private static RunHistoryEntry CreateRun() => CreateRun(_startedAt);

    /// <summary>
    /// Creates a run that started at <paramref name="startedAt"/>, with a new identifier, no filter, no items and no
    /// discovery events.
    /// </summary>
    /// <param name="startedAt">The time the run started.</param>
    /// <returns>The run.</returns>
    private static RunHistoryEntry CreateRun(DateTimeOffset startedAt) =>
        new(Guid.NewGuid(), startedAt, startedAt.AddMinutes(2), RunMode.Interactive, false, ExitCode.Success);
}
