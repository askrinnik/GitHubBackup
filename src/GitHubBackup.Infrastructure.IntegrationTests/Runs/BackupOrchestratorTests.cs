using System.IO.Abstractions;
using System.IO.Compression;
using GitHubBackup.Core;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Archiving;
using GitHubBackup.Infrastructure.BackupConfiguration;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.Infrastructure.State;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace GitHubBackup.Infrastructure.IntegrationTests.Runs;

/// <summary>
/// Tests <see cref="BackupOrchestrator"/> with the real git client, 7-Zip archiver, JSON stores and discovery services
/// against local bare repositories and a <see cref="FakeGitHubApi"/>.
/// </summary>
public sealed class BackupOrchestratorTests : IDisposable
{
    private const string _owner = "owner";

    private readonly LocalGitSandbox _sandbox = new();
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

    /// <summary>
    /// Deletes the sandbox.
    /// </summary>
    public void Dispose() => _sandbox.Dispose();

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
    /// Runs the orchestrator over the real services, then moves the clock a minute on so the next run names its
    /// archive differently.
    /// </summary>
    /// <returns>The result of the run.</returns>
    private async Task<BackupRunResult> RunAsync()
    {
        var masker = new SecretMasker();
        var runner = new ProcessRunner(masker, _fileSystem, TimeProvider.System, NullLogger<ProcessRunner>.Instance);
        var gitClient = new GitClient(runner, _fileSystem, masker, NullLogger<GitClient>.Instance);
        var backupOptions = Options.Create(new BackupOptions());

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
            new CloneRelocator(_fileSystem, gitClient, NullLogger<CloneRelocator>.Instance),
            _fileSystem,
            NullLogger<DiscoveryDecisionApplier>.Instance);

        var orchestrator = new BackupOrchestrator(
            _configStore,
            toolLocator,
            tokenProvider,
            pathResolver,
            exclusionMatcher,
            discoveryService,
            decisionApplier,
            remoteUrlProvider,
            _gitHubApi,
            new ChangeDetector(gitClient, _fileSystem, NullLogger<ChangeDetector>.Instance),
            new ArchiveNameBuilder(_timeProvider, backupOptions),
            new SevenZipArchiver(runner, _fileSystem, backupOptions, NullLogger<SevenZipArchiver>.Instance),
            _stateStore,
            _fileSystem,
            _timeProvider,
            NullLogger<BackupOrchestrator>.Instance);

        var result = await orchestrator.RunAsync(
            new(BackupRunFilter.All, new AutomaticDiscoveryDecisionHandler()), CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        return result;
    }
}
