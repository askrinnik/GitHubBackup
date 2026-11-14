using System.Globalization;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using GitHubBackup.Core;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Clones;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Archiving;
using GitHubBackup.Infrastructure.BackupConfiguration;
using GitHubBackup.Infrastructure.Clones;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.IntegrationTests.History;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.Infrastructure.Runs;
using GitHubBackup.Infrastructure.State;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;
using GitHubBackup.TestUtilities.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace GitHubBackup.Infrastructure.IntegrationTests.Runs;

/// <summary>
/// Tests <see cref="BackupOrchestrator"/> with the real git client, 7-Zip archiver, JSON stores and discovery services
/// against local bare repositories and a <see cref="FakeGitHubApi"/>, alone and wrapped in a
/// <see cref="RecordingBackupOrchestrator"/> over a SQLite run history.
/// </summary>
public sealed class BackupOrchestratorTests : IDisposable
{
    private const string _owner = "owner";

    private readonly LocalGitSandbox _sandbox = new();
    private readonly TestTelemetry _telemetry = new();
    private readonly FileSystem _fileSystem = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, string> _remoteUrls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _wikiUrls = new(StringComparer.Ordinal);
    private readonly HashSet<string> _wikiEnabled = new(StringComparer.Ordinal);
    private readonly FakeGitHubApi _gitHubApi = new();
    private readonly string _archiveRoot;
    private readonly string _sourcesRoot;
    private readonly JsonBackupConfigStore _configStore;
    private readonly JsonBackupStateStore _stateStore;

    /// <summary>
    /// Creates the stores over a configuration file, an archive root and a clone root inside the sandbox.
    /// </summary>
    public BackupOrchestratorTests()
    {
        _archiveRoot = Path.Combine(_sandbox.RootPath, "archives");
        _sourcesRoot = Path.Combine(_sandbox.RootPath, "sources");
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_sandbox.RootPath);
        var writer = new AtomicFileWriter(_fileSystem);
        _configStore = new(
            _fileSystem,
            Options.Create(new BackupOptions { ConfigPath = Path.Combine(_sandbox.RootPath, "backup-config.json") }),
            environment,
            new BackupConfigValidator(),
            writer,
            NullLogger<JsonBackupConfigStore>.Instance);
        _stateStore = new(_fileSystem, writer, NullLogger<JsonBackupStateStore>.Instance);
        _timeProvider.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunAsync_FullCycle_ArchivesThenUpToDateThenArchivesChange()
    {
        var remote = await AddRemoteAsync("repo");
        await SaveConfigAsync("repo");

        var first = await RunAsync();
        var second = await RunAsync();
        var head = await remote.CommitAsync("change.txt", "changed", "Change", CancellationToken);
        var third = await RunAsync();

        first.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        second.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.UpToDate);
        var changed = third.Items.ShouldHaveSingleItem();
        changed.Status.ShouldBe(BackupItemStatus.Archived);
        third.ExitCode.ShouldBe(ExitCode.Success);

        var archiveFolder = Path.Combine(_archiveRoot, _owner, "repo");
        Directory.GetFiles(archiveFolder, "*.zip").Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(
            [first.Items[0].ArchiveFileName, changed.ArchiveFileName]);
        var state = await _stateStore.LoadAsync(archiveFolder, 1, "owner/repo", CancellationToken);
        state.Repository.Archives.Select(archive => archive.File).ShouldBe(
            [first.Items[0].ArchiveFileName!, changed.ArchiveFileName!]);
        state.Repository.LastArchive.ShouldNotBeNull().HeadCommit.ShouldBe(head);
    }

    [Fact]
    public async Task RunAsync_OneRemoteMissing_ReportsFailedAndArchivesOther()
    {
        _remoteUrls["missing"] = MissingRemoteUrl("missing");
        await AddRemoteAsync("present");
        await SaveConfigAsync("missing", "present");

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
        [
            ("owner/missing", BackupItemStatus.Failed),
            ("owner/present", BackupItemStatus.Archived),
        ]);
        result.Items[0].Message.ShouldNotBeNullOrWhiteSpace();
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
        Directory.Exists(Path.Combine(_archiveRoot, _owner, "missing")).ShouldBeFalse();
        Directory.GetFiles(Path.Combine(_archiveRoot, _owner, "present"), "*.zip").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RunAsync_LeftoverInTemporaryFolder_IsDeletedAtStart()
    {
        await AddRemoteAsync("repo");
        await SaveConfigAsync("repo");
        var temporaryFolder = Path.Combine(_sourcesRoot, RepositoryPathResolver.TemporaryArchiveFolderName);
        Directory.CreateDirectory(temporaryFolder);
        var leftover = Path.Combine(temporaryFolder, "owner_repo_20261110-235959_0123456789.abc.zip");
        await File.WriteAllTextAsync(leftover, "partial archive", CancellationToken);

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        File.Exists(leftover).ShouldBeFalse();
        Directory.GetFileSystemEntries(temporaryFolder).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_WikiChangesAlone_ArchivesOnlyWiki()
    {
        await AddRemoteAsync("repo");
        var wiki = await AddWikiAsync("repo");
        await SaveConfigAsync("repo");

        var first = await RunAsync();
        await wiki.CommitAsync("Home.md", "# Home", "Edit wiki", CancellationToken);
        var second = await RunAsync();

        first.Items.Select(item => (item.Repository, item.Status, item.Target)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Archived, BackupTarget.Repository),
            ("owner/repo.wiki", BackupItemStatus.Archived, BackupTarget.Wiki),
        ]);
        second.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.UpToDate),
            ("owner/repo.wiki", BackupItemStatus.Archived),
        ]);
        second.ExitCode.ShouldBe(ExitCode.Success);

        var archiveFolder = Path.Combine(_archiveRoot, _owner, "repo");
        var state = await _stateStore.LoadAsync(archiveFolder, 1, "owner/repo", CancellationToken);
        state.Repository.Archives.Select(archive => archive.File).ShouldBe([first.Items[0].ArchiveFileName!]);
        state.Wiki.Archives.Select(archive => archive.File).ShouldBe(
            [first.Items[1].ArchiveFileName!, second.Items[1].ArchiveFileName!]);
        var wikiArchive = second.Items[1].ArchiveFileName.ShouldNotBeNull();
        wikiArchive.ShouldStartWith("owner_repo.wiki_");
        Directory.Exists(Path.Combine(_sourcesRoot, _owner, "repo.wiki", ".git")).ShouldBeTrue();
        using var zip = ZipFile.OpenRead(Path.Combine(archiveFolder, wikiArchive));
        zip.Entries.Select(entry => entry.FullName.Split('/', '\\')[0]).Distinct().ShouldBe(["repo.wiki"]);
        zip.Entries.ShouldContain(entry => entry.FullName.Replace('\\', '/') == "repo.wiki/Home.md");
    }

    [Fact]
    public async Task RunAsync_WikiMissing_DoesNotFail()
    {
        await AddRemoteAsync("repo");
        _wikiEnabled.Add("repo");
        await SaveConfigAsync("repo");

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        result.ExitCode.ShouldBe(ExitCode.Success);
        Directory.Exists(Path.Combine(_sourcesRoot, _owner, "repo.wiki")).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_WikiDeletedAfterArchive_SkipsAndKeepsCloneAndArchive()
    {
        await AddRemoteAsync("repo");
        var wiki = await AddWikiAsync("repo");
        await SaveConfigAsync("repo");
        var first = await RunAsync();
        TemporaryFolder.Delete(wiki.BarePath);

        var second = await RunAsync();

        second.Items.ShouldHaveSingleItem().Repository.ShouldBe("owner/repo");
        second.ExitCode.ShouldBe(ExitCode.Success);
        var archiveFolder = Path.Combine(_archiveRoot, _owner, "repo");
        File.Exists(Path.Combine(archiveFolder, first.Items[1].ArchiveFileName!)).ShouldBeTrue();
        Directory.Exists(Path.Combine(_sourcesRoot, _owner, "repo.wiki", ".git")).ShouldBeTrue();
        var state = await _stateStore.LoadAsync(archiveFolder, 1, "owner/repo", CancellationToken);
        state.Wiki.Archives.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RunAsync_NewAccountRepository_IsAddedToConfigAndBackedUp()
    {
        await AddRemoteAsync("fresh");
        _gitHubApi.AddAccount(_owner, GitHubAccountType.User);
        _gitHubApi.AddRepository(7, _owner, "fresh", hasWiki: false);
        await _configStore.SaveAsync(
            new BackupConfig
            {
                BackupRoot = _archiveRoot,
                SourcesRoot = _sourcesRoot,
                Accounts = [new() { Url = $"https://github.com/{_owner}" }],
            },
            CancellationToken);

        var first = await RunAsync();
        var second = await RunAsync();

        first.Items.Select(item => (item.Repository, item.Status)).ShouldBe([("owner/fresh", BackupItemStatus.Archived)]);
        first.Discovery.ShouldNotBeNull().Added.ShouldBe(["owner/fresh"]);
        first.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
        second.Items.Select(item => (item.Repository, item.Status)).ShouldBe([("owner/fresh", BackupItemStatus.UpToDate)]);
        second.Discovery.ShouldBeNull();
        second.ExitCode.ShouldBe(ExitCode.Success);

        var saved = await _configStore.LoadAsync(CancellationToken);
        var entry = saved.Config.Accounts.ShouldHaveSingleItem().Repositories.ShouldHaveSingleItem();
        (entry.Name, entry.Id, entry.Status).ShouldBe(("fresh", 7L, RepositoryStatus.Active));
        Directory.GetFiles(Path.Combine(_archiveRoot, _owner, "fresh"), "*.zip").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RunAsync_NewRepositoryWithInvalidName_IsIgnoredAndRunContinues()
    {
        await AddRemoteAsync("fresh");
        _gitHubApi.AddAccount(_owner, GitHubAccountType.User);
        _gitHubApi.AddRepository(7, _owner, "fresh", hasWiki: false);
        _gitHubApi.AddRepository(8, _owner, "con", hasWiki: false);
        await _configStore.SaveAsync(
            new BackupConfig
            {
                BackupRoot = _archiveRoot,
                SourcesRoot = _sourcesRoot,
                Accounts = [new() { Url = $"https://github.com/{_owner}" }],
            },
            CancellationToken);

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe([("owner/fresh", BackupItemStatus.Archived)]);
        result.Discovery.ShouldNotBeNull().Added.ShouldBe(["owner/fresh"]);
        var saved = await _configStore.LoadAsync(CancellationToken);
        saved.Config.Accounts.ShouldHaveSingleItem().Repositories.ShouldHaveSingleItem().Name.ShouldBe("fresh");
        Directory.GetFiles(Path.Combine(_archiveRoot, _owner, "fresh"), "*.zip").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RunAsync_RenamedRepositorySilent_KeepsArchiveFolderAndMovesClone()
    {
        var remote = await AddRemoteAsync("old");
        await SaveConfigAsync("old");
        var first = await RunAsync();
        _gitHubApi.AddRepository(1, _owner, "new", hasWiki: false);
        _remoteUrls["new"] = remote.Url;
        await remote.CommitAsync("change.txt", "changed", "Change", CancellationToken);

        var second = await RunAsync();

        var outcome = second.Discovery.ShouldNotBeNull().Renamed.ShouldHaveSingleItem();
        (outcome.OldFullName, outcome.NewFullName, outcome.Applied).ShouldBe(
            ("owner/old", "owner/new", RenameDecision.KeepArchiveFolder));
        second.Items.Select(item => (item.Repository, item.Status)).ShouldBe([("owner/new", BackupItemStatus.Archived)]);
        second.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);

        var saved = await _configStore.LoadAsync(CancellationToken);
        var entry = saved.Config.Repositories.ShouldHaveSingleItem();
        entry.Url.ShouldBe($"https://github.com/{_owner}/new");
        var oldFolder = Path.Combine(_archiveRoot, _owner, "old");
        entry.Path.ShouldBe(oldFolder);
        Directory.GetFiles(oldFolder, "*.zip").Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(
            new[] { first.Items[0].ArchiveFileName, second.Items[0].ArchiveFileName }.Order(StringComparer.Ordinal));
        Directory.Exists(Path.Combine(_archiveRoot, _owner, "new")).ShouldBeFalse();
        Directory.Exists(Path.Combine(_sourcesRoot, _owner, "new", ".git")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_sourcesRoot, _owner, "old")).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_MissingRepositorySilent_MarksUnavailableAndKeepsFiles()
    {
        await AddRemoteAsync("gone");
        await SaveConfigAsync("gone");
        var first = await RunAsync();
        _gitHubApi.RemoveRepository(1);

        var second = await RunAsync();

        second.Discovery.ShouldNotBeNull().MarkedUnavailable.ShouldBe(["owner/gone"]);
        var item = second.Items.ShouldHaveSingleItem();
        (item.Repository, item.Status).ShouldBe(("owner/gone", BackupItemStatus.Skipped));
        second.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);

        var saved = await _configStore.LoadAsync(CancellationToken);
        saved.Config.Repositories.ShouldHaveSingleItem().Status.ShouldBe(RepositoryStatus.Unavailable);
        var archiveFolder = Path.Combine(_archiveRoot, _owner, "gone");
        File.Exists(Path.Combine(archiveFolder, first.Items[0].ArchiveFileName!)).ShouldBeTrue();
        Directory.Exists(Path.Combine(_sourcesRoot, _owner, "gone", ".git")).ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_DryRun_ChangesNothingOnDisk()
    {
        var changed = await AddRemoteAsync("a");
        await AddRemoteAsync("b");
        _gitHubApi.AddAccount(_owner, GitHubAccountType.User);
        _gitHubApi.AddRepository(1, _owner, "a", hasWiki: false);
        await SaveAccountConfigAsync("a");
        await RunAsync();
        _gitHubApi.AddRepository(2, _owner, "b", hasWiki: false);
        _gitHubApi.AddRepository(4, _owner, "fresh", hasWiki: false);
        await SaveAccountConfigAsync("a", "b", "gone");
        var head = await changed.CommitAsync("change.txt", "changed", "Change", CancellationToken);
        var temporaryFolder = Path.Combine(_sourcesRoot, RepositoryPathResolver.TemporaryArchiveFolderName);
        Directory.CreateDirectory(temporaryFolder);
        await File.WriteAllTextAsync(Path.Combine(temporaryFolder, "leftover.zip"), "partial archive", CancellationToken);
        var configPath = Path.Combine(_sandbox.RootPath, "backup-config.json");
        var statePath = Path.Combine(_archiveRoot, _owner, "a", BackupState.FileName);
        var configBefore = await File.ReadAllBytesAsync(configPath, CancellationToken);
        var stateBefore = await File.ReadAllBytesAsync(statePath, CancellationToken);
        var before = SnapshotTree();

        var result = await RunAsync(dryRun: true);

        SnapshotTree().ShouldBe(before);
        (await File.ReadAllBytesAsync(configPath, CancellationToken)).ShouldBe(configBefore);
        (await File.ReadAllBytesAsync(statePath, CancellationToken)).ShouldBe(stateBefore);
        result.IsDryRun.ShouldBeTrue();
        result.Items.Select(item => (item.Repository, item.Status, item.ArchiveFileName)).ShouldBe(
        [
            ("owner/a", BackupItemStatus.WouldArchive, $"owner_a_20261111-120100_{head[..10]}.zip"),
            ("owner/b", BackupItemStatus.WouldClone, null),
            ("owner/gone", BackupItemStatus.Skipped, null),
        ]);
        var planned = result.PlannedDiscovery.ShouldNotBeNull();
        planned.NewInAccounts.Select(item => item.Repository.FullName).ShouldBe(["owner/fresh"]);
        planned.Missing.Select(item => item.FullName).ShouldBe(["owner/gone"]);
        result.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
    }

    [Fact]
    public async Task RunAsync_DryRunUnchanged_ReportsUpToDate()
    {
        await AddRemoteAsync("repo");
        await SaveConfigAsync("repo");
        await RunAsync();

        var result = await RunAsync(dryRun: true);

        result.Items.Select(item => (item.Repository, item.Status, item.ArchiveFileName)).ShouldBe(
            [("owner/repo", BackupItemStatus.UpToDate, null)]);
        result.PlannedDiscovery.ShouldBeNull();
        result.ExitCode.ShouldBe(ExitCode.Success);
    }

    [Fact]
    public async Task RunAsync_DryRunWiki_ReportsWikiStatus()
    {
        await AddRemoteAsync("repo");
        var wiki = await AddWikiAsync("repo");
        await SaveConfigAsync("repo");
        await RunAsync();
        var head = await wiki.CommitAsync("Home.md", "# Home", "Edit wiki", CancellationToken);
        var before = SnapshotTree();

        var result = await RunAsync(dryRun: true);

        result.Items.Select(item => (item.Repository, item.Status, item.ArchiveFileName, item.Target)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.UpToDate, null, BackupTarget.Repository),
            ("owner/repo.wiki", BackupItemStatus.WouldArchive, $"owner_repo.wiki_20261111-120100_{head[..10]}.zip", BackupTarget.Wiki),
        ]);
        SnapshotTree().ShouldBe(before);
    }

    [Fact]
    public async Task RunAsync_Recorded_StoresItemsWithBranchHeadChangedRefsAndAutomaticDecision()
    {
        var remote = await AddRemoteAsync("fresh");
        _gitHubApi.AddAccount(_owner, GitHubAccountType.User);
        _gitHubApi.AddRepository(7, _owner, "fresh", hasWiki: false);
        await SaveAccountConfigAsync();
        using var database = new TemporaryHistoryDatabase();
        var store = await database.CreateMigratedStoreAsync(CancellationToken);
        var firstRunId = Guid.NewGuid();
        var secondRunId = Guid.NewGuid();

        var first = await RunRecordedAsync(store, firstRunId);
        await remote.CreateTagAsync("v1", null, annotated: false, force: false, CancellationToken);
        var head = await remote.CommitAsync("change.txt", "changed", "Change", CancellationToken);
        var second = await RunRecordedAsync(store, secondRunId);

        var firstEntry = (await store.GetAsync(firstRunId, CancellationToken)).ShouldNotBeNull();
        firstEntry.Mode.ShouldBe(RunMode.Silent);
        firstEntry.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
        var firstItem = firstEntry.Items.ShouldHaveSingleItem();
        (firstItem.Repository, firstItem.Status, firstItem.Branch).ShouldBe(("owner/fresh", BackupItemStatus.Archived, "main"));
        firstItem.ArchiveFileName.ShouldBe(first.Items[0].ArchiveFileName);
        firstItem.HeadCommit.ShouldNotBeNullOrWhiteSpace();
        firstItem.ChangedRefs.ShouldBe(["heads/main"]);
        var discoveryEvent = firstEntry.DiscoveryEvents.ShouldHaveSingleItem();
        (discoveryEvent.Repository, discoveryEvent.Kind, discoveryEvent.Decision, discoveryEvent.DecidedBy).ShouldBe(
            ("owner/fresh", DiscoveryEventKind.New, DiscoveryEventDecision.Added, DecisionMaker.Automatic));

        var secondEntry = (await store.GetAsync(secondRunId, CancellationToken)).ShouldNotBeNull();
        secondEntry.ExitCode.ShouldBe(ExitCode.Success);
        secondEntry.DiscoveryEvents.ShouldBeEmpty();
        var secondItem = secondEntry.Items.ShouldHaveSingleItem();
        secondItem.ArchiveFileName.ShouldBe(second.Items[0].ArchiveFileName);
        secondItem.HeadCommit.ShouldBe(head);
        secondItem.ChangedRefs.ShouldBe(["heads/main", "tags/v1"]);
    }

    [Fact]
    public async Task RunAsync_StandaloneEntryWithoutId_WritesIdKeepsPreviousFileAndArchives()
    {
        await AddRemoteAsync("repo");
        _gitHubApi.AddRepository(5, _owner, "repo", hasWiki: false);
        await SaveConfigWithoutIdAsync("repo");
        var configPath = Path.Combine(_sandbox.RootPath, "backup-config.json");
        var before = await File.ReadAllTextAsync(configPath, CancellationToken);

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe([("owner/repo", BackupItemStatus.Archived)]);
        result.ExitCode.ShouldBe(ExitCode.Success);
        result.SavedRepositoryIds.ShouldBe(["owner/repo"]);
        (await File.ReadAllTextAsync(configPath, CancellationToken)).ShouldBe(WithId(before, "repo", 5));
        (await File.ReadAllTextAsync(configPath + ".bak", CancellationToken)).ShouldBe(before);
        var archiveFolder = Path.Combine(_archiveRoot, _owner, "repo");
        Directory.GetFiles(archiveFolder, "*.zip").ShouldHaveSingleItem();
        var state = await _stateStore.LoadAsync(archiveFolder, 5, "owner/repo", CancellationToken);
        state.Repository.Archives.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_DryRunEntryWithoutId_WritesOnlyConfirmedIdAndChecksRepository(bool confirm)
    {
        await AddRemoteAsync("repo");
        _gitHubApi.AddRepository(5, _owner, "repo", hasWiki: false);
        await SaveConfigWithoutIdAsync("repo");
        var configPath = Path.Combine(_sandbox.RootPath, "backup-config.json");
        var before = await File.ReadAllTextAsync(configPath, CancellationToken);
        var handler = Substitute.For<IDiscoveryDecisionHandler>();
        handler.ConfirmIdSaveAsync(Arg.Any<RepositoryIdProposal>(), Arg.Any<CancellationToken>()).Returns(confirm);

        var result = await CreateOrchestrator().RunAsync(
            new(BackupRunFilter.All, handler) { DryRun = true, Mode = RunMode.Interactive }, CancellationToken);

        await handler.Received(1).ConfirmIdSaveAsync(new("owner/repo", 5), Arg.Any<CancellationToken>());
        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe([("owner/repo", BackupItemStatus.WouldClone)]);
        result.SavedRepositoryIds.ShouldBe(confirm ? ["owner/repo"] : []);
        (await File.ReadAllTextAsync(configPath, CancellationToken)).ShouldBe(confirm ? WithId(before, "repo", 5) : before);
        File.Exists(configPath + ".bak").ShouldBe(confirm);
        Directory.Exists(_sourcesRoot).ShouldBeFalse();
        Directory.Exists(_archiveRoot).ShouldBeFalse();
    }

    /// <summary>
    /// The kinds of local work a clone can hold.
    /// </summary>
    public enum LocalChange
    {
        /// <summary>A tracked file is modified.</summary>
        Uncommitted,

        /// <summary>A file git neither tracks nor ignores is added.</summary>
        Untracked,

        /// <summary>Another branch is checked out.</summary>
        OtherBranch,

        /// <summary>A commit is made that the server does not have.</summary>
        UnpushedCommit,
    }

    [Fact]
    public async Task RunAsync_ClonePath_ClonesIntoItWithWikiNextToItAndArchivesUnderFolderName()
    {
        var remote = await AddRemoteAsync("repo");
        await AddWikiAsync("repo");
        var clonePath = WorkFolder("MyRepo");
        await SaveClonePathConfigAsync(clonePath);

        var first = await RunAsync();
        var head = await remote.CommitAsync("change.txt", "changed", "Change", CancellationToken);
        var second = await RunAsync();

        first.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
            [("owner/repo", BackupItemStatus.Archived), ("owner/repo.wiki", BackupItemStatus.Archived)]);
        second.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
            [("owner/repo", BackupItemStatus.Archived), ("owner/repo.wiki", BackupItemStatus.UpToDate)]);
        Directory.Exists(Path.Combine(clonePath, ".git")).ShouldBeTrue();
        Directory.Exists(Path.Combine(clonePath + ".wiki", ".git")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_sourcesRoot, _owner)).ShouldBeFalse();
        (await HeadAsync(clonePath)).ShouldBe(head);
        var archiveFolder = Path.Combine(_archiveRoot, _owner, "repo");
        ZipRoots(Path.Combine(archiveFolder, second.Items[0].ArchiveFileName!)).ShouldBe(["MyRepo"]);
        ZipRoots(Path.Combine(archiveFolder, first.Items[1].ArchiveFileName!)).ShouldBe(["MyRepo.wiki"]);
        File.ReadAllText(Path.Combine(_sourcesRoot, CloneLocations.FileName)).ShouldContain(clonePath.Replace(@"\", @"\\", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "A")]
    [InlineData("A", "B")]
    [InlineData("A", null)]
    public async Task RunAsync_ClonePathChanged_MovesCloneAndKeepsBackingUp(string? before, string? after)
    {
        await AddRemoteAsync("repo");
        var oldFolder = before is null ? Path.Combine(_sourcesRoot, _owner, "repo") : WorkFolder(before);
        var newFolder = after is null ? Path.Combine(_sourcesRoot, _owner, "repo") : WorkFolder(after);
        await SaveClonePathConfigAsync(before is null ? null : oldFolder);
        await RunAsync();
        var head = await HeadAsync(oldFolder);
        await SaveClonePathConfigAsync(after is null ? null : newFolder);

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.UpToDate);
        Directory.Exists(oldFolder).ShouldBeFalse();
        (await HeadAsync(newFolder)).ShouldBe(head);
        (await GitAsync(newFolder, "remote", "get-url", "origin")).ShouldBe([_remoteUrls["repo"]]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ServerForcePushed_UpdatesCleanCloneWithoutCountingDroppedCommits(bool withClonePath)
    {
        var remote = await AddRemoteAsync("repo");
        var initial = (await remote.GetRemoteRefsAsync(CancellationToken))["refs/heads/main"];
        await remote.CommitAsync("dropped.txt", "dropped", "Commit to drop", CancellationToken);
        var folder = withClonePath ? WorkFolder("MyRepo") : Path.Combine(_sourcesRoot, _owner, "repo");
        await SaveClonePathConfigAsync(withClonePath ? folder : null);
        await RunAsync();
        await remote.ForcePushAsync("main", initial, CancellationToken);
        var head = await remote.CommitAsync("rewritten.txt", "rewritten", "Rewritten commit", CancellationToken);

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        (await HeadAsync(folder)).ShouldBe(head);
        File.Exists(Path.Combine(folder, "dropped.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_ClonePathRemovedWhileWorkingCopyHasIgnoredFiles_FailsAndDoesNotMove()
    {
        var remote = await AddRemoteAsync("repo");
        await remote.CommitAsync(".gitignore", ".env\n", "Ignore settings", CancellationToken);
        var clonePath = WorkFolder("MyRepo");
        await SaveClonePathConfigAsync(clonePath);
        await RunAsync();
        File.WriteAllText(Path.Combine(clonePath, ".env"), "secret settings");
        await SaveClonePathConfigAsync(null);

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Failed);
        item.Message.ShouldNotBeNull().ShouldStartWith($"The clone '{clonePath}' holds ignored files (.env)");
        File.ReadAllText(Path.Combine(clonePath, ".env")).ShouldBe("secret settings");
        Directory.Exists(Path.Combine(_sourcesRoot, _owner, "repo")).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_NewClonePathOccupied_FailsAndDeletesNothing()
    {
        await AddRemoteAsync("repo");
        await AddRemoteAsync("other");
        var oldFolder = WorkFolder("A");
        var newFolder = WorkFolder("B");
        await SaveClonePathConfigAsync(oldFolder, withOther: true);
        await RunAsync();
        Directory.CreateDirectory(newFolder);
        File.WriteAllText(Path.Combine(newFolder, "mine.txt"), "mine");
        await SaveClonePathConfigAsync(newFolder, withOther: true);

        var result = await RunAsync();

        var item = result.Items[0];
        (item.Repository, item.Status).ShouldBe(("owner/repo", BackupItemStatus.Failed));
        item.Message.ShouldNotBeNull().ShouldContain("the target folder already exists");
        result.Items[1].Status.ShouldBe(BackupItemStatus.UpToDate);
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
        Directory.Exists(Path.Combine(oldFolder, ".git")).ShouldBeTrue();
        Directory.GetFileSystemEntries(newFolder).ShouldBe([Path.Combine(newFolder, "mine.txt")]);
    }

    [Fact]
    public async Task RunAsync_DryRunWithChangedClonePath_ReportsMoveAndChangesNothing()
    {
        await AddRemoteAsync("repo");
        var oldFolder = WorkFolder("A");
        var newFolder = WorkFolder("B");
        await SaveClonePathConfigAsync(oldFolder);
        await RunAsync();
        await SaveClonePathConfigAsync(newFolder);
        var before = SnapshotTree();

        var result = await RunAsync(dryRun: true);

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.UpToDate);
        item.Message.ShouldBe($"The clone would be moved from '{oldFolder}' to '{newFolder}'.");
        SnapshotTree().ShouldBe(before);
    }

    [Theory]
    [InlineData(LocalChange.Uncommitted)]
    [InlineData(LocalChange.Untracked)]
    [InlineData(LocalChange.OtherBranch)]
    [InlineData(LocalChange.UnpushedCommit)]
    public async Task RunAsync_LocalChangeInClonePath_FailsWithReasonAndBacksUpOthers(LocalChange change)
    {
        var remote = await AddRemoteAsync("repo");
        var other = await AddRemoteAsync("other");
        var clonePath = WorkFolder("MyRepo");
        await SaveClonePathConfigAsync(clonePath, withOther: true);
        await RunAsync();
        await MakeLocalChangeAsync(clonePath, change);
        var cloneBefore = await CloneStateAsync(clonePath);
        await remote.CommitAsync("server.txt", "server", "Server change", CancellationToken);
        await other.CommitAsync("server.txt", "server", "Server change", CancellationToken);

        var result = await RunAsync();

        var item = result.Items[0];
        (item.Repository, item.Status, item.ArchiveFileName).ShouldBe(("owner/repo", BackupItemStatus.Failed, null));
        item.Message.ShouldBe(
            $"The clone '{clonePath}' has local changes: {Problem(change)}. Commit and push them or switch the branch, then run the backup again.");
        (result.Items[1].Repository, result.Items[1].Status).ShouldBe(("owner/other", BackupItemStatus.Archived));
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
        (await CloneStateAsync(clonePath)).ShouldBe(cloneBefore);
        var state = await _stateStore.LoadAsync(Path.Combine(_archiveRoot, _owner, "repo"), 1, "owner/repo", CancellationToken);
        state.Repository.Archives.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(false, BackupTarget.Repository)]
    [InlineData(false, BackupTarget.Wiki)]
    [InlineData(true, BackupTarget.Repository)]
    [InlineData(true, BackupTarget.Wiki)]
    public async Task RunAsync_UncommittedChangeInAnyClone_FailsOnlyThatItem(bool withClonePath, BackupTarget target)
    {
        var remote = await AddRemoteAsync("repo");
        var wiki = await AddWikiAsync("repo");
        var clonePath = WorkFolder("MyRepo");
        await SaveClonePathConfigAsync(withClonePath ? clonePath : null);
        await RunAsync();
        var repositoryFolder = withClonePath ? clonePath : Path.Combine(_sourcesRoot, _owner, "repo");
        var changedFolder = target == BackupTarget.Wiki ? repositoryFolder + ".wiki" : repositoryFolder;
        await MakeLocalChangeAsync(changedFolder, LocalChange.Uncommitted);
        await remote.CommitAsync("server.txt", "server", "Server change", CancellationToken);
        await wiki.CommitAsync("server.md", "server", "Server change", CancellationToken);

        var result = await RunAsync();

        result.Items.Select(item => (item.Target, item.Status)).ShouldBe(
        [
            (BackupTarget.Repository, target == BackupTarget.Repository ? BackupItemStatus.Failed : BackupItemStatus.Archived),
            (BackupTarget.Wiki, target == BackupTarget.Wiki ? BackupItemStatus.Failed : BackupItemStatus.Archived),
        ]);
        result.Items.Single(item => item.Target == target).Message.ShouldNotBeNull()
            .ShouldStartWith($"The clone '{changedFolder}' has local changes: uncommitted changes.");
        File.ReadAllText(Path.Combine(changedFolder, "README.md")).ShouldBe("local edit");
    }

    [Fact]
    public async Task RunAsync_LocalChangeAndClonePathChanged_DoesNotMove()
    {
        await AddRemoteAsync("repo");
        var oldFolder = WorkFolder("A");
        var newFolder = WorkFolder("B");
        await SaveClonePathConfigAsync(oldFolder);
        await RunAsync();
        await MakeLocalChangeAsync(oldFolder, LocalChange.Untracked);
        await SaveClonePathConfigAsync(newFolder);

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Failed);
        item.Message.ShouldNotBeNull().ShouldStartWith($"The clone '{oldFolder}' has local changes: untracked files.");
        File.Exists(Path.Combine(oldFolder, "notes.txt")).ShouldBeTrue();
        Directory.Exists(newFolder).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_LocalChangeAndRepositoryRenamed_KeepsCloneUnderOldName()
    {
        var remote = await AddRemoteAsync("old");
        await SaveConfigAsync("old");
        await RunAsync();
        var oldFolder = Path.Combine(_sourcesRoot, _owner, "old");
        await MakeLocalChangeAsync(oldFolder, LocalChange.Uncommitted);
        _gitHubApi.AddRepository(1, _owner, "new", hasWiki: false);
        _remoteUrls["new"] = remote.Url;

        var result = await RunAsync();

        result.Discovery.ShouldNotBeNull().Renamed.ShouldHaveSingleItem().CloneRelocation
            .ShouldBe(CloneRelocationResult.LocalChanges);
        var item = result.Items.ShouldHaveSingleItem();
        (item.Repository, item.Status).ShouldBe(("owner/new", BackupItemStatus.Failed));
        item.Message.ShouldNotBeNull().ShouldStartWith($"The clone '{oldFolder}' has local changes: uncommitted changes.");
        File.ReadAllText(Path.Combine(oldFolder, "README.md")).ShouldBe("local edit");
        Directory.Exists(Path.Combine(_sourcesRoot, _owner, "new")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_IgnoredFiles_KeptAndLeftOutOfArchiveOnlyInClonePath(bool withClonePath)
    {
        var remote = await AddRemoteAsync("repo");
        await remote.CommitAsync(".gitignore", "*.log\nbin/\n", "Ignore build output", CancellationToken);
        var folder = withClonePath ? WorkFolder("MyRepo") : Path.Combine(_sourcesRoot, _owner, "repo");
        await SaveClonePathConfigAsync(withClonePath ? folder : null);
        await RunAsync();
        File.WriteAllText(Path.Combine(folder, "build.log"), "log");
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        File.WriteAllText(Path.Combine(folder, "bin", "app.dll"), "binary");
        await remote.CommitAsync("change.txt", "changed", "Change", CancellationToken);

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Archived);
        File.Exists(Path.Combine(folder, "build.log")).ShouldBe(withClonePath);
        File.Exists(Path.Combine(folder, "bin", "app.dll")).ShouldBe(withClonePath);
        using var zip = ZipFile.OpenRead(Path.Combine(_archiveRoot, _owner, "repo", item.ArchiveFileName!));
        var entries = zip.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
        entries.ShouldContain(entry => entry.EndsWith("/change.txt", StringComparison.Ordinal));
        entries.ShouldContain(entry => entry.Contains("/.git/", StringComparison.Ordinal));
        entries.ShouldNotContain(entry => entry.EndsWith("build.log", StringComparison.Ordinal));
        entries.ShouldNotContain(entry => entry.Contains("/bin/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_DryRunWithLocalChange_ReportsSameFailureAndChangesNothing()
    {
        var remote = await AddRemoteAsync("repo");
        var clonePath = WorkFolder("MyRepo");
        await SaveClonePathConfigAsync(clonePath);
        await RunAsync();
        await MakeLocalChangeAsync(clonePath, LocalChange.OtherBranch);
        await remote.CommitAsync("server.txt", "server", "Server change", CancellationToken);
        var before = SnapshotTree();

        var result = await RunAsync(dryRun: true);

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Failed);
        item.Message.ShouldNotBeNull().ShouldStartWith(
            $"The clone '{clonePath}' has local changes: branch 'feature' instead of 'main'.");
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
        SnapshotTree().ShouldBe(before);
    }

    [Fact]
    public async Task RunAsync_LocalChangeAndServerUnchanged_ReportsUpToDateWithoutTouchingClone()
    {
        await AddRemoteAsync("repo");
        var clonePath = WorkFolder("MyRepo");
        await SaveClonePathConfigAsync(clonePath);
        await RunAsync();
        await MakeLocalChangeAsync(clonePath, LocalChange.Uncommitted);

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.UpToDate);
        File.ReadAllText(Path.Combine(clonePath, "README.md")).ShouldBe("local edit");
    }

    [Fact]
    public async Task RunAsync_ClonePathNotEmptyWithoutGit_FailsAndLeavesFolder()
    {
        await AddRemoteAsync("repo");
        var clonePath = WorkFolder("MyRepo");
        Directory.CreateDirectory(clonePath);
        File.WriteAllText(Path.Combine(clonePath, "mine.txt"), "mine");
        await SaveClonePathConfigAsync(clonePath);

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Failed);
        item.Message.ShouldNotBeNull().ShouldStartWith($"The folder '{clonePath}' is not empty and holds no git clone");
        Directory.GetFileSystemEntries(clonePath).ShouldBe([Path.Combine(clonePath, "mine.txt")]);
    }

    [Fact]
    public async Task RunAsync_ClonePathEmpty_ClonesIntoIt()
    {
        await AddRemoteAsync("repo");
        var clonePath = WorkFolder("MyRepo");
        Directory.CreateDirectory(clonePath);
        await SaveClonePathConfigAsync(clonePath);

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        Directory.Exists(Path.Combine(clonePath, ".git")).ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_ExistingWorkingCopy_IsUsedAsClone()
    {
        var remote = await AddRemoteAsync("repo");
        var clonePath = WorkFolder("MyRepo");
        Directory.CreateDirectory(Path.GetDirectoryName(clonePath)!);
        await GitAsync(Path.GetDirectoryName(clonePath)!, "clone", remote.Url, clonePath);
        await SaveClonePathConfigAsync(clonePath);

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        (await HeadAsync(clonePath)).ShouldBe((await remote.GetRemoteRefsAsync(CancellationToken))["refs/heads/main"]);
    }

    [Fact]
    public async Task RunAsync_WorkingCopyOfOtherRepository_FailsBeforeReadingServer()
    {
        await AddRemoteAsync("repo");
        var foreign = await _sandbox.CreateRemoteAsync("foreign", CancellationToken);
        var clonePath = WorkFolder("MyRepo");
        Directory.CreateDirectory(Path.GetDirectoryName(clonePath)!);
        await GitAsync(Path.GetDirectoryName(clonePath)!, "clone", foreign.Url, clonePath);
        await SaveClonePathConfigAsync(clonePath);

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Failed);
        item.Message.ShouldNotBeNull().ShouldStartWith($"The clone '{clonePath}' has an origin that does not point at");
        (await GitAsync(clonePath, "remote", "get-url", "origin")).ShouldBe([foreign.Url]);
    }

    /// <summary>
    /// Returns a folder for working copies inside the sandbox, outside the clone root and the archive root.
    /// </summary>
    /// <param name="name">The name of the folder.</param>
    /// <returns>The absolute folder; it is not created.</returns>
    private string WorkFolder(string name) => Path.Combine(_sandbox.RootPath, "work", name);

    /// <summary>
    /// Writes <c>backup-config.json</c> with the standalone repository <c>repo</c> (id 1) and, when asked, the
    /// standalone repository <c>other</c> (id 2), and registers both with the fake API.
    /// </summary>
    /// <param name="clonePath">The <c>clonePath</c> of <c>repo</c>, or <see langword="null"/>.</param>
    /// <param name="withOther">Whether <c>other</c> is listed too.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private Task SaveClonePathConfigAsync(string? clonePath, bool withOther = false)
    {
        _gitHubApi.AddRepository(1, _owner, "repo", _wikiEnabled.Contains("repo"));
        List<RepositoryConfig> repositories =
            [new() { Url = $"https://github.com/{_owner}/repo", Id = 1, ClonePath = clonePath }];
        if (withOther)
        {
            _gitHubApi.AddRepository(2, _owner, "other", hasWiki: false);
            repositories.Add(new() { Url = $"https://github.com/{_owner}/other", Id = 2 });
        }

        return _configStore.SaveAsync(
            new BackupConfig { BackupRoot = _archiveRoot, SourcesRoot = _sourcesRoot, Repositories = repositories },
            CancellationToken);
    }

    /// <summary>
    /// Makes one kind of local work in a clone with the git of the sandbox.
    /// </summary>
    /// <param name="clone">The clone folder.</param>
    /// <param name="change">The kind of work.</param>
    /// <returns>A task that completes when the work is made.</returns>
    private async Task MakeLocalChangeAsync(string clone, LocalChange change)
    {
        switch (change)
        {
            case LocalChange.Uncommitted:
                await File.WriteAllTextAsync(Path.Combine(clone, "README.md"), "local edit", CancellationToken);
                break;
            case LocalChange.Untracked:
                await File.WriteAllTextAsync(Path.Combine(clone, "notes.txt"), "notes", CancellationToken);
                break;
            case LocalChange.OtherBranch:
                await GitAsync(clone, "checkout", "-b", "feature");
                break;
            default:
                await File.WriteAllTextAsync(Path.Combine(clone, "local.txt"), "local", CancellationToken);
                await GitAsync(clone, "add", "local.txt");
                await GitAsync(clone, "commit", "-m", "Local commit");
                break;
        }
    }

    /// <summary>
    /// Returns the problem the check reports for one kind of local work made by <see cref="MakeLocalChangeAsync"/>.
    /// </summary>
    /// <param name="change">The kind of work.</param>
    /// <returns>The problem text.</returns>
    private static string Problem(LocalChange change) => change switch
    {
        LocalChange.Uncommitted => "uncommitted changes",
        LocalChange.Untracked => "untracked files",
        LocalChange.OtherBranch => "branch 'feature' instead of 'main'",
        _ => "1 unpushed commit(s)",
    };

    /// <summary>
    /// Describes what an update would change in a clone: the commit, the branch and the status of the files.
    /// </summary>
    /// <param name="clone">The clone folder.</param>
    /// <returns>The description.</returns>
    private async Task<string> CloneStateAsync(string clone) =>
        string.Join(
            '\n',
            [
                await HeadAsync(clone),
                .. await GitAsync(clone, "rev-parse", "--abbrev-ref", "HEAD"),
                .. await GitAsync(clone, "status", "--porcelain", "--untracked-files=all"),
            ]);

    /// <summary>
    /// Returns the commit <c>HEAD</c> of a clone points at.
    /// </summary>
    /// <param name="clone">The clone folder.</param>
    /// <returns>The commit id.</returns>
    private async Task<string> HeadAsync(string clone) => (await GitAsync(clone, "rev-parse", "HEAD")).Single();

    /// <summary>
    /// Runs git in the isolated environment of the sandbox.
    /// </summary>
    /// <param name="workingDirectory">The folder git runs in.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The non-empty lines of standard output.</returns>
    private Task<IReadOnlyList<string>> GitAsync(string workingDirectory, params string[] arguments) =>
        _sandbox.RunGitAsync(workingDirectory, CancellationToken, arguments);

    /// <summary>
    /// Returns the distinct root folders of the entries of a ZIP archive.
    /// </summary>
    /// <param name="archive">The archive file.</param>
    /// <returns>The root folder names.</returns>
    private static List<string> ZipRoots(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return [.. zip.Entries.Select(entry => entry.FullName.Split('/', '\\')[0]).Distinct()];
    }

    /// <summary>
    /// Deletes the sandbox.
    /// </summary>
    public void Dispose()
    {
        _telemetry.Dispose();
        _sandbox.Dispose();
    }

    /// <summary>
    /// Returns a <c>file://</c> URL inside the sandbox where no repository exists.
    /// </summary>
    /// <param name="name">The name of the missing repository.</param>
    /// <returns>The URL.</returns>
    private string MissingRemoteUrl(string name) =>
        new Uri(Path.Combine(_sandbox.RootPath, "remotes", "missing", name + ".git")).AbsoluteUri;

    /// <summary>
    /// Creates a local bare repository with one commit as the wiki of the repository <paramref name="name"/> and
    /// enables its wiki feature.
    /// </summary>
    /// <param name="name">The repository name.</param>
    /// <returns>The remote of the wiki.</returns>
    private async Task<LocalGitRemote> AddWikiAsync(string name)
    {
        var remote = await _sandbox.CreateRemoteAsync(name + ".wiki", CancellationToken);
        _wikiUrls[name] = remote.Url;
        _wikiEnabled.Add(name);
        return remote;
    }

    /// <summary>
    /// Creates a local bare repository with one commit and routes the repository <paramref name="name"/> to it.
    /// </summary>
    /// <param name="name">The repository name.</param>
    /// <returns>The remote.</returns>
    private async Task<LocalGitRemote> AddRemoteAsync(string name)
    {
        var remote = await _sandbox.CreateRemoteAsync(name, CancellationToken);
        _remoteUrls[name] = remote.Url;
        return remote;
    }

    /// <summary>
    /// Writes <c>backup-config.json</c> with one standalone repository of <see cref="_owner"/> per name, ids from 1,
    /// and registers each repository with the fake API so discovery finds it under its configured name.
    /// </summary>
    /// <param name="names">The repository names.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private Task SaveConfigAsync(params string[] names)
    {
        for (var index = 0; index < names.Length; index++)
        {
            _gitHubApi.AddRepository(index + 1, _owner, names[index], _wikiEnabled.Contains(names[index]));
        }

        return _configStore.SaveAsync(
            new BackupConfig
            {
                BackupRoot = _archiveRoot,
                SourcesRoot = _sourcesRoot,
                Repositories =
                [
                    .. names.Select((name, index) => new RepositoryConfig
                    {
                        Url = $"https://github.com/{_owner}/{name}",
                        Id = index + 1,
                    }),
                ],
            },
            CancellationToken);
    }

    /// <summary>
    /// Writes <c>backup-config.json</c> with one standalone repository of <see cref="_owner"/> per name, all without
    /// an id.
    /// </summary>
    /// <param name="names">The repository names.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private Task SaveConfigWithoutIdAsync(params string[] names) =>
        _configStore.SaveAsync(
            new BackupConfig
            {
                BackupRoot = _archiveRoot,
                SourcesRoot = _sourcesRoot,
                Repositories = [.. names.Select(name => new RepositoryConfig { Url = $"https://github.com/{_owner}/{name}" })],
            },
            CancellationToken);

    /// <summary>
    /// Returns the text of <c>backup-config.json</c> with the id of one standalone repository inserted after its URL,
    /// where the store writes it.
    /// </summary>
    /// <param name="json">The text without the id.</param>
    /// <param name="name">The repository name.</param>
    /// <param name="id">The id.</param>
    /// <returns>The text with the id.</returns>
    private static string WithId(string json, string name, long id)
    {
        var url = $"\"url\": \"https://github.com/{_owner}/{name}\",";
        json.ShouldContain(url);
        return json.Replace(
            url, string.Create(CultureInfo.InvariantCulture, $"{url}\r\n      \"id\": {id},"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes <c>backup-config.json</c> with the account <see cref="_owner"/> that lists one repository per name, ids
    /// from 1.
    /// </summary>
    /// <param name="names">The repository names.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private Task SaveAccountConfigAsync(params string[] names) =>
        _configStore.SaveAsync(
            new BackupConfig
            {
                BackupRoot = _archiveRoot,
                SourcesRoot = _sourcesRoot,
                Accounts =
                [
                    new()
                    {
                        Url = $"https://github.com/{_owner}",
                        Repositories = [.. names.Select((name, index) => new AccountRepositoryConfig { Name = name, Id = index + 1 })],
                    },
                ],
            },
            CancellationToken);

    /// <summary>
    /// Describes every folder and file of the sandbox: the last write time of each folder, and the size, the last
    /// write time and a hash of the content of each file.
    /// </summary>
    /// <returns>The description of each entry, by path relative to the sandbox and in ordinal order.</returns>
    private SortedDictionary<string, string> SnapshotTree()
    {
        var root = _sandbox.RootPath;
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            entries[Path.GetRelativePath(root, directory)] =
                Directory.GetLastWriteTimeUtc(directory).Ticks.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            entries[Path.GetRelativePath(root, file)] = string.Create(
                CultureInfo.InvariantCulture,
                $"{bytes.Length} {File.GetLastWriteTimeUtc(file).Ticks} {Convert.ToHexString(SHA256.HashData(bytes))}");
        }

        return entries;
    }

    /// <summary>
    /// Runs the orchestrator over the real services, then moves the clock a minute on so the next run names its
    /// archive differently.
    /// </summary>
    /// <param name="dryRun">Whether the run is a dry run.</param>
    /// <returns>The result of the run.</returns>
    private async Task<BackupRunResult> RunAsync(bool dryRun = false)
    {
        var result = await CreateOrchestrator().RunAsync(
            new(BackupRunFilter.All, new AutomaticDiscoveryDecisionHandler()) { DryRun = dryRun }, CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        return result;
    }

    /// <summary>
    /// Runs the orchestrator in silent mode through a <see cref="RecordingBackupOrchestrator"/> that writes to
    /// <paramref name="historyStore"/>, then moves the clock a minute on.
    /// </summary>
    /// <param name="historyStore">The run history.</param>
    /// <param name="runId">The identifier of the run.</param>
    /// <returns>The result of the run.</returns>
    private async Task<BackupRunResult> RunRecordedAsync(IRunHistoryStore historyStore, Guid runId)
    {
        var runContext = Substitute.For<IRunContext>();
        runContext.RunId.Returns(runId);
        var recording = new RecordingBackupOrchestrator(
            CreateOrchestrator(), historyStore, runContext, _timeProvider, NullLogger<RecordingBackupOrchestrator>.Instance);
        var result = await recording.RunAsync(
            new(BackupRunFilter.All, new AutomaticDiscoveryDecisionHandler()) { Mode = RunMode.Silent }, CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        return result;
    }

    /// <summary>
    /// Creates the orchestrator over the real services.
    /// </summary>
    /// <returns>The orchestrator.</returns>
    private BackupOrchestrator CreateOrchestrator()
    {
        var masker = new SecretMasker();
        var runner = new ProcessRunner(masker, _fileSystem, TimeProvider.System, NullLogger<ProcessRunner>.Instance);
        var gitClient = new GitClient(runner, _fileSystem, masker, NullLogger<GitClient>.Instance);
        var backupOptions = Options.Create(new BackupOptions());
        var cloneGuard = new CloneGuard(gitClient, NullLogger<CloneGuard>.Instance);
        var cloneRelocator = new CloneRelocator(_fileSystem, gitClient, cloneGuard, NullLogger<CloneRelocator>.Instance);

        var toolLocator = Substitute.For<IToolLocator>();
        toolLocator.LocateAsync(Arg.Any<CancellationToken>()).Returns(
            new ToolSet(new(_sandbox.GitPath, "test"), "test", new(ToolsOptions.DefaultSevenZipPath, "test")));
        var tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(GitHubToken.Anonymous);
        var remoteUrlProvider = Substitute.For<IRemoteUrlProvider>();
        remoteUrlProvider.GetRepositoryUrl(_owner, Arg.Any<string>()).Returns(call => _remoteUrls[call.ArgAt<string>(1)]);
        remoteUrlProvider.GetWikiUrl(_owner, Arg.Any<string>()).Returns(call =>
            _wikiUrls.GetValueOrDefault(call.ArgAt<string>(1)) ?? MissingRemoteUrl(call.ArgAt<string>(1) + ".wiki"));
        var pathResolver = new RepositoryPathResolver(Path.Combine(_sandbox.RootPath, BackupConfig.DefaultSourcesFolderName));
        var exclusionMatcher = new ExclusionMatcher();
        var discoveryService = new RepositoryDiscoveryService(
            _gitHubApi, exclusionMatcher, NullLogger<RepositoryDiscoveryService>.Instance);
        var decisionApplier = new DiscoveryDecisionApplier(
            _configStore,
            pathResolver,
            remoteUrlProvider,
            cloneRelocator,
            _fileSystem,
            NullLogger<DiscoveryDecisionApplier>.Instance);

        return new BackupOrchestrator(
            _configStore,
            toolLocator,
            tokenProvider,
            new NamedMutexRunLock($@"Local\GitHubBackup.Tests.{Guid.NewGuid():N}", NullLogger<NamedMutexRunLock>.Instance),
            pathResolver,
            exclusionMatcher,
            discoveryService,
            decisionApplier,
            remoteUrlProvider,
            _gitHubApi,
            new ChangeDetector(gitClient, cloneGuard, _fileSystem, _telemetry.Telemetry, NullLogger<ChangeDetector>.Instance),
            cloneRelocator,
            new JsonCloneLocationStore(_fileSystem, new AtomicFileWriter(_fileSystem), NullLogger<JsonCloneLocationStore>.Instance),
            gitClient,
            new ArchiveNameBuilder(_timeProvider, backupOptions),
            new SevenZipArchiver(runner, _fileSystem, backupOptions, NullLogger<SevenZipArchiver>.Instance),
            _stateStore,
            _fileSystem,
            _timeProvider,
            _telemetry.Telemetry,
            NullLogger<BackupOrchestrator>.Instance);
    }
}
