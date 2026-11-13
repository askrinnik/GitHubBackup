using System.IO.Abstractions;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.State;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;
using GitHubBackup.TestUtilities.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Infrastructure.IntegrationTests.Changes;

/// <summary>
/// Tests <see cref="ChangeDetector"/> with the real <see cref="GitClient"/> and the installed git against local bare
/// repositories.
/// </summary>
public sealed class ChangeDetectorTests : IDisposable
{
    private const string _archiveFile = "repo.7z";
    private const int _startingEventId = 1;

    private readonly LocalGitSandbox _sandbox = new();
    private readonly TestTelemetry _telemetry = new();
    private readonly SecretMasker _masker = new();
    private readonly FakeLogger<ProcessRunner> _runnerLogger = new();
    private readonly string _clonePath;
    private readonly string _archiveFolder;

    /// <summary>
    /// Creates the sandbox with a clone folder whose parent does not exist yet and an empty archive folder.
    /// </summary>
    public ChangeDetectorTests()
    {
        _clonePath = Path.Combine(_sandbox.RootPath, "backup", "owner", "repo");
        _archiveFolder = _sandbox.CreateFolder("archives");
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DetectAsync_NonDefaultBranchAdvanced_ArchivesRefsChange()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        var archive = await ArchiveFirstAsync(remote);
        await remote.CheckoutAsync("develop", CancellationToken);
        await remote.CommitAsync("dev.txt", "dev", "Develop commit", CancellationToken);

        var result = await DetectAsync(remote, archive);

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.RefsChanged);
        result.Branch.ShouldBe("main");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetectAsync_TagCreatedOrMoved_ArchivesRefsChange(bool move)
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateTagAsync("v1", null, annotated: false, force: false, CancellationToken);
        var archive = await ArchiveFirstAsync(remote);
        if (move)
        {
            await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);
            await remote.CreateTagAsync("v1", null, annotated: false, force: true, CancellationToken);
        }
        else
        {
            await remote.CreateTagAsync("v2", null, annotated: false, force: false, CancellationToken);
        }

        var result = await DetectAsync(remote, archive);

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.RefsChanged);
    }

    [Fact]
    public async Task DetectAsync_ServerUnchanged_ReportsUpToDateWithoutFetch()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var archive = await ArchiveFirstAsync(remote);
        _runnerLogger.Collector.Clear();

        var result = await DetectAsync(remote, archive);

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        result.Reason.ShouldBe(ChangeReason.Unchanged);
        var commands = _runnerLogger.Collector.GetSnapshot()
            .Where(record => record.Id.Id == _startingEventId)
            .Select(record => record.GetStructuredStateValue("CommandLine")!)
            .ToList();
        commands.ShouldContain(message => message.Contains("ls-remote"));
        commands.ShouldNotContain(message => message.Contains(" fetch"));
    }

    [Fact]
    public async Task DetectAsync_CloneDeletedAndServerUnchanged_ClonesWithoutNewArchive()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var archive = await ArchiveFirstAsync(remote);
        TemporaryFolder.Delete(_clonePath);

        var result = await DetectAsync(remote, archive);

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        Directory.Exists(Path.Combine(_clonePath, ".git")).ShouldBeTrue();
    }

    [Fact]
    public async Task DetectAsync_ArchiveFileDeleted_ArchivesAgain()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var archive = await ArchiveFirstAsync(remote);
        File.Delete(Path.Combine(_archiveFolder, _archiveFile));

        var result = await DetectAsync(remote, archive);

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.ArchiveFileMissing);
    }

    [Fact]
    public async Task DetectAsync_EmptyRepository_NothingToArchive()
    {
        var remote = await _sandbox.CreateEmptyRemoteAsync("repo", CancellationToken);

        var result = await DetectAsync(remote, last: null);

        result.Decision.ShouldBe(ChangeDecision.NothingToArchive);
        result.Reason.ShouldBe(ChangeReason.EmptyRepository);
        result.Branch.ShouldBeNull();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _telemetry.Dispose();
        _sandbox.Dispose();
    }

    /// <summary>
    /// Detects the first state of the remote and records the archive the orchestrator would create for it.
    /// </summary>
    /// <param name="remote">The remote repository.</param>
    /// <returns>The archive record, whose file exists in the archive folder.</returns>
    private async Task<ArchiveRecord> ArchiveFirstAsync(LocalGitRemote remote)
    {
        var first = await DetectAsync(remote, last: null);
        first.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        first.Reason.ShouldBe(ChangeReason.NoArchive);

        await File.WriteAllTextAsync(Path.Combine(_archiveFolder, _archiveFile), "archive", CancellationToken);
        return new ArchiveRecord(_archiveFile, DateTimeOffset.UtcNow, first.Branch!, "0123456789abcdef", 7, first.Refs);
    }

    /// <summary>
    /// Runs the detector over the real file system and the real process runner.
    /// </summary>
    /// <param name="remote">The remote repository.</param>
    /// <param name="last">The last archive, or <see langword="null"/> when there is none.</param>
    /// <returns>The detection result.</returns>
    private Task<ChangeDetection> DetectAsync(LocalGitRemote remote, ArchiveRecord? last)
    {
        var fileSystem = new FileSystem();
        var client = new GitClient(
            new ProcessRunner(_masker, fileSystem, TimeProvider.System, _runnerLogger),
            fileSystem,
            _masker,
            NullLogger<GitClient>.Instance)
        {
            AdditionalConfiguration = [new("protocol.file.allow", "always")],
        };
        var request = new ChangeDetectionRequest
        {
            Git = new GitRepositoryRequest { GitPath = _sandbox.GitPath, ClonePath = _clonePath, Branch = "main" },
            RemoteUrl = remote.Url,
            ArchiveFolder = _archiveFolder,
            LastArchive = last,
        };

        return new ChangeDetector(client, fileSystem, _telemetry.Telemetry, NullLogger<ChangeDetector>.Instance).DetectAsync(request, CancellationToken);
    }
}
