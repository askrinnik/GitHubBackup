using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.History;

/// <summary>
/// Tests <see cref="ArchiveSummaryProvider"/> over a substituted overview provider and archive history provider.
/// </summary>
public sealed class ArchiveSummaryProviderTests
{
    private static readonly RepositoryOverviewEntry _tool = CreateEntry("octo", "tool", "octo", 1);

    private static readonly RepositoryOverviewEntry _app = CreateEntry("octo", "app", "octo", 2);

    private static readonly RepositoryOverviewEntry _lib = CreateEntry("other", "lib", null, 3);

    private readonly IRepositoryOverviewProvider _overviewProvider = Substitute.For<IRepositoryOverviewProvider>();

    private readonly IArchiveHistoryProvider _historyProvider = Substitute.For<IArchiveHistoryProvider>();

    private readonly IBackupStateStore _stateStore = Substitute.For<IBackupStateStore>();

    private static readonly string[] _expectedNames = ["octo/tool", "octo/app", "other/lib"];

    private readonly IRunHistoryStore _runHistoryStore = Substitute.For<IRunHistoryStore>();

    private readonly FakeLogger<ArchiveSummaryProvider> _logger = new();

    private readonly ArchiveSummaryProvider _provider;

    /// <summary>
    /// Creates the provider over an overview with two account repositories and one standalone repository, whose
    /// histories are empty.
    /// </summary>
    public ArchiveSummaryProviderTests()
    {
        _overviewProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(new RepositoryOverview(
            [new("octo", "https://github.com/octo", [_tool, _app])],
            [_lib]));
        _historyProvider.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ArchiveHistory(call.ArgAt<string>(0), true, []));
        _stateStore.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => BackupState.CreateEmpty(call.ArgAt<long>(1), call.ArgAt<string>(2)));
        _runHistoryStore.GetLatestRepositoryRunsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, LatestRepositoryRun>());
        var nameBuilder = Substitute.For<IArchiveNameBuilder>();
        nameBuilder.TryParse(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<ParsedArchiveName?>())
            .Returns(call =>
            {
                call[3] = new ParsedArchiveName(BackupTarget.Repository, DateTimeOffset.UnixEpoch, "abcdef0123");
                return true;
            });
        _provider = new(_overviewProvider, _historyProvider, _stateStore, _runHistoryStore, nameBuilder, _logger);
    }

    [Fact]
    public async Task LoadAsync_LatestRuns_SetsWikiStatusFromOneHistoryQuery()
    {
        var runs = new Dictionary<string, LatestRepositoryRun>(StringComparer.OrdinalIgnoreCase)
        {
            ["octo/tool"] = new(
                new("octo/tool", BackupTarget.Repository, BackupItemStatus.UpToDate, TimeSpan.Zero),
                new("octo/tool", BackupTarget.Wiki, BackupItemStatus.Skipped, TimeSpan.Zero)),
            ["other/lib"] = new(new("other/lib", BackupTarget.Repository, BackupItemStatus.Archived, TimeSpan.Zero), null),
        };
        _runHistoryStore.GetLatestRepositoryRunsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(runs);

        var summaries = await LoadAsync();

        summaries.Select(summary => summary.WikiStatus).ShouldBe([WikiStatus.NoPages, WikiStatus.Unknown, WikiStatus.Off]);
        await _runHistoryStore.Received(1).GetLatestRepositoryRunsAsync(
            Arg.Is<IReadOnlyCollection<string>>(names => names.SequenceEqual(_expectedNames)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_RunHistoryUnreadable_AllWikiStatusesUnknownAndLogs()
    {
        _runHistoryStore.GetLatestRepositoryRunsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RunHistoryException(@"C:\app\history.db", "The runs cannot be read."));

        var summaries = await LoadAsync();

        summaries.ShouldAllBe(summary => summary.WikiStatus == WikiStatus.Unknown && summary.Error == null);
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task LoadAsync_Overview_ReturnsRepositoriesInConfigurationOrder()
    {
        var summaries = await LoadAsync();

        summaries.Select(summary => summary.Repository).ShouldBe([_tool, _app, _lib]);
        summaries.ShouldAllBe(summary => summary.Error == null && summary.History != null);
    }

    [Fact]
    public async Task LoadAsync_Repository_ReadsHistoryOfItsArchiveFolder()
    {
        var history = new ArchiveHistory(_app.ArchiveFolder, false, []);
        _historyProvider.LoadAsync(_app.ArchiveFolder, 2, "octo/app", Arg.Any<CancellationToken>()).Returns(history);

        var summaries = await LoadAsync();

        summaries[1].History.ShouldBeSameAs(history);
    }

    [Fact]
    public async Task LoadAsync_OldArchives_SummaryHoldsCleanupPlan()
    {
        var old = new ArchiveRecord("old.zip", new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), "main", "a1b2c3", 10, RefSnapshot.Empty);
        var last = new ArchiveRecord("last.zip", new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero), "main", "a1b2c3", 10, RefSnapshot.Empty);
        var state = BackupState.CreateEmpty(1, "octo/tool")
            .WithArchive(BackupTarget.Repository, old, "octo/tool")
            .WithArchive(BackupTarget.Repository, last, "octo/tool");
        _stateStore.LoadAsync(_tool.ArchiveFolder, 1, "octo/tool", Arg.Any<CancellationToken>()).Returns(state);
        _historyProvider.LoadAsync(_tool.ArchiveFolder, 1, "octo/tool", Arg.Any<CancellationToken>()).Returns(
            new ArchiveHistory(_tool.ArchiveFolder, true, [Entry(last), Entry(old)]));

        var summaries = await LoadAsync();

        summaries[0].CleanupPlan.ShouldNotBeNull().FilesToRecycle.ShouldHaveSingleItem().File.ShouldBe("old.zip");
        summaries[1].CleanupPlan.ShouldNotBeNull().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_StateUnreadableForPlan_ErrorInItsRow()
    {
        var error = new BackupStateException(@"C:\backup\octo\tool\.backup-state.json", ["The file is not valid JSON."]);
        _stateStore.LoadAsync(_tool.ArchiveFolder, Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);

        var summaries = await LoadAsync();

        summaries[0].Error.ShouldBeSameAs(error);
        summaries[0].CleanupPlan.ShouldBeNull();
    }

    [Theory]
    [InlineData(nameof(BackupStateException))]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(UnauthorizedAccessException))]
    public async Task LoadAsync_HistoryOfOneRepositoryFails_ErrorInItsRowOnly(string kind)
    {
        Exception error = kind switch
        {
            nameof(BackupStateException) => new BackupStateException(
                @"C:\backup\octo\tool\.backup-state.json", ["The file is not valid JSON."]),
            nameof(IOException) => new IOException("The folder cannot be listed."),
            _ => new UnauthorizedAccessException("Access is denied."),
        };
        _historyProvider.LoadAsync(_tool.ArchiveFolder, Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);

        var summaries = await LoadAsync();

        summaries[0].Error.ShouldBeSameAs(error);
        summaries[0].History.ShouldBeNull();
        summaries[0].CleanupPlan.ShouldBeNull();
        summaries.Skip(1).ShouldAllBe(summary => summary.Error == null && summary.History != null);
    }

    [Fact]
    public async Task LoadAsync_ConfigFails_Propagates()
    {
        var error = new BackupConfigException(@"C:\app\backup-config.json", ["broken"]);
        _overviewProvider.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(error);

        var exception = await Should.ThrowAsync<BackupConfigException>(LoadAsync);

        exception.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task LoadAsync_Cancelled_Propagates()
    {
        _historyProvider.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(LoadAsync);
    }

    /// <summary>
    /// Loads the summaries.
    /// </summary>
    /// <returns>The summaries.</returns>
    private Task<IReadOnlyList<RepositoryArchiveSummary>> LoadAsync() =>
        _provider.LoadAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Creates the history entry of a recorded archive of <c>octo/tool</c> whose file is on the disk.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns>The entry.</returns>
    private static ArchiveHistoryEntry Entry(ArchiveRecord record) => new(
        BackupTarget.Repository,
        record.File,
        Path.Combine(_tool.ArchiveFolder, record.File),
        record.CreatedAt,
        record.Branch,
        record.HeadCommit,
        record.SizeBytes,
        [],
        FileExists: true,
        IsInState: true,
        record.SizeBytes);

    /// <summary>
    /// Creates a repository of the overview whose archive folder is below <c>C:\backup</c>.
    /// </summary>
    /// <param name="owner">The owner.</param>
    /// <param name="name">The name.</param>
    /// <param name="account">The account that lists the repository, or <see langword="null"/>.</param>
    /// <param name="id">The GitHub id.</param>
    /// <returns>The repository.</returns>
    private static RepositoryOverviewEntry CreateEntry(string owner, string name, string? account, long id) =>
        new(owner, name, account, RepositoryOverviewStatus.Active, null, null)
        {
            RepositoryId = id,
            ArchiveFolder = $@"C:\backup\{owner}\{name}",
        };
}
