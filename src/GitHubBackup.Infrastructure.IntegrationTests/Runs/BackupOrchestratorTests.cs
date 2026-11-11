using System.IO.Abstractions;
using GitHubBackup.Core;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Archiving;
using GitHubBackup.Infrastructure.BackupConfiguration;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.Infrastructure.State;
using GitHubBackup.TestUtilities.Git;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace GitHubBackup.Infrastructure.IntegrationTests.Runs;

/// <summary>
/// Tests <see cref="BackupOrchestrator"/> with the real git client, 7-Zip archiver and JSON stores against local bare
/// repositories.
/// </summary>
public sealed class BackupOrchestratorTests : IDisposable
{
    private const string _owner = "owner";

    private readonly LocalGitSandbox _sandbox = new();
    private readonly FileSystem _fileSystem = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, string> _remoteUrls = new(StringComparer.Ordinal);
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
        _remoteUrls["missing"] = new Uri(Path.Combine(_sandbox.RootPath, "remotes", "missing.git")).AbsoluteUri;
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

    /// <summary>
    /// Deletes the sandbox.
    /// </summary>
    public void Dispose() => _sandbox.Dispose();

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
    /// Writes <c>backup-config.json</c> with one standalone repository of <see cref="_owner"/> per name, ids from 1.
    /// </summary>
    /// <param name="names">The repository names.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private Task SaveConfigAsync(params string[] names) => _configStore.SaveAsync(
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

        var orchestrator = new BackupOrchestrator(
            _configStore,
            toolLocator,
            tokenProvider,
            new RepositoryPathResolver(Path.Combine(_sandbox.RootPath, BackupConfig.DefaultSourcesFolderName)),
            new ExclusionMatcher(),
            remoteUrlProvider,
            new ChangeDetector(gitClient, _fileSystem, NullLogger<ChangeDetector>.Instance),
            new ArchiveNameBuilder(_timeProvider, backupOptions),
            new SevenZipArchiver(runner, _fileSystem, backupOptions, NullLogger<SevenZipArchiver>.Instance),
            _stateStore,
            _fileSystem,
            _timeProvider,
            NullLogger<BackupOrchestrator>.Instance);

        var result = await orchestrator.RunAsync(CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        return result;
    }
}
