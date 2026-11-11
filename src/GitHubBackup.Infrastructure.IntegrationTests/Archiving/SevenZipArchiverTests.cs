using System.IO.Abstractions;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Archiving;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.IntegrationTests.Processes;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.TestUtilities.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.IntegrationTests.Archiving;

/// <summary>
/// Tests <see cref="SevenZipArchiver"/> with the installed 7-Zip and git against a local repository.
/// </summary>
public sealed class SevenZipArchiverTests : IDisposable
{
    private const string _fileName = "owner_repo_20261111-132030_0123456789.zip";

    private readonly LocalGitSandbox _sandbox = new();
    private readonly CapturingLogger<ProcessRunner> _runnerLogger = new();
    private readonly string _temporaryFolder;
    private readonly string _archiveFolder;

    /// <summary>
    /// Creates the temporary and archive folders inside the sandbox.
    /// </summary>
    public SevenZipArchiverTests()
    {
        _temporaryFolder = Path.Combine(_sandbox.RootPath, "sources", ".tmp");
        _archiveFolder = Path.Combine(_sandbox.RootPath, "archives");
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateAsync_Clone_ArchiveExtractsIntoWorkingRepository()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var head = await remote.CommitAsync("docs/a.txt", "content", "Second commit", CancellationToken);
        await remote.CreateTagAsync("v1", null, annotated: true, force: false, CancellationToken);
        var clone = await CloneAsync(remote, "repo");

        var result = await CreateArchiver().CreateAsync(Request(clone), CancellationToken);

        result.Path.ShouldBe(Path.Combine(_archiveFolder, _fileName));
        result.SizeBytes.ShouldBe(new FileInfo(result.Path).Length);
        Directory.GetFileSystemEntries(_archiveFolder).ShouldBe([result.Path]);
        Directory.GetFileSystemEntries(_temporaryFolder).ShouldBeEmpty();

        var extracted = await ExtractAsync(result.Path);
        Directory.GetFileSystemEntries(extracted).ShouldBe([Path.Combine(extracted, "repo")]);
        var restored = Path.Combine(extracted, "repo");
        Directory.Exists(Path.Combine(restored, ".git")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(restored, "docs", "a.txt")).ShouldBe("content");
        await _sandbox.RunGitAsync(restored, CancellationToken, "fsck", "--full");
        (await _sandbox.RunGitAsync(restored, CancellationToken, "status", "--porcelain")).ShouldBeEmpty();
        (await _sandbox.RunGitAsync(restored, CancellationToken, "rev-parse", "HEAD")).ShouldBe([head]);
        (await _sandbox.RunGitAsync(restored, CancellationToken, "tag")).ShouldBe(["v1"]);
    }

    [Fact]
    public async Task CreateAsync_Clone_LogsSevenZipOutputWithToolProperty()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var clone = await CloneAsync(remote, "repo");

        await CreateArchiver().CreateAsync(Request(clone), CancellationToken);

        var entries = _runnerLogger.Entries.Where(entry => entry.Properties.TryGetValue("Tool", out var tool) && Equals(tool, "7z")).ToList();
        entries.ShouldContain(entry => entry.Message.Contains("Starting", StringComparison.Ordinal));
        entries.ShouldContain(entry => entry.Message.Contains("Everything is Ok", StringComparison.Ordinal));
        entries.ShouldContain(entry => entry.Message.Contains("exited with code 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_FolderNameStartsWithDash_ArchivesFolder()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var clone = await CloneAsync(remote, "-repo");

        var result = await CreateArchiver().CreateAsync(Request(clone), CancellationToken);

        var extracted = await ExtractAsync(result.Path);
        Directory.Exists(Path.Combine(extracted, "-repo", ".git")).ShouldBeTrue();
        File.Exists(Path.Combine(extracted, "-repo", "README.md")).ShouldBeTrue();
    }

    /// <inheritdoc />
    public void Dispose() => _sandbox.Dispose();

    /// <summary>
    /// Clones the remote into a folder of the sandbox.
    /// </summary>
    /// <param name="remote">The remote to clone.</param>
    /// <param name="folderName">The name of the clone folder.</param>
    /// <returns>The path of the clone folder.</returns>
    private async Task<string> CloneAsync(LocalGitRemote remote, string folderName)
    {
        var parent = Path.Combine(_sandbox.RootPath, "sources", "owner");
        Directory.CreateDirectory(parent);
        await _sandbox.RunGitAsync(parent, CancellationToken, "clone", "--", remote.Url, folderName);
        return Path.Combine(parent, folderName);
    }

    /// <summary>
    /// Extracts an archive into a new folder with 7-Zip.
    /// </summary>
    /// <param name="archive">The archive path.</param>
    /// <returns>The folder that holds the extracted entries.</returns>
    private async Task<string> ExtractAsync(string archive)
    {
        var target = Path.Combine(_sandbox.RootPath, "extracted", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        var result = await CreateRunner(new CapturingLogger<ProcessRunner>()).RunAsync(
            new ProcessRequest
            {
                FileName = ToolsOptions.DefaultSevenZipPath,
                Arguments = ["x", "-y", "-o" + target, "--", archive],
                WorkingDirectory = _sandbox.RootPath,
            },
            CancellationToken);
        result.ExitCode.ShouldBe(0);
        return target;
    }

    private ArchiveRequest Request(string clone) => new()
    {
        SevenZipPath = ToolsOptions.DefaultSevenZipPath,
        SourceFolder = clone,
        TemporaryFolder = _temporaryFolder,
        DestinationFolder = _archiveFolder,
        FileName = _fileName,
    };

    private SevenZipArchiver CreateArchiver() => new(
        CreateRunner(_runnerLogger), new FileSystem(), Options.Create(new BackupOptions()), NullLogger<SevenZipArchiver>.Instance);

    private static ProcessRunner CreateRunner(ILogger<ProcessRunner> logger) =>
        new(new SecretMasker(), new FileSystem(), TimeProvider.System, logger);
}
