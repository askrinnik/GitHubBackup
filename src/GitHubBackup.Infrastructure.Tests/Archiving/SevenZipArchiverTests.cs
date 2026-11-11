using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Processes;
using GitHubBackup.Infrastructure.Archiving;
using GitHubBackup.Infrastructure.Tests.BackupConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Archiving;

/// <summary>
/// Tests <see cref="SevenZipArchiver"/> with a mocked file system and a mocked process runner.
/// </summary>
public sealed class SevenZipArchiverTests
{
    private const string _sevenZip = @"C:\tools\7z.exe";
    private const string _source = @"C:\backup\owner\repo";
    private const string _temporary = @"C:\backup\.tmp";
    private const string _destination = @"C:\archives";
    private const string _fileName = "owner_repo_20261111-132030_0123456789.zip";

    private readonly FaultInjectingFileSystem _fileSystem = new();
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private const int _movingEventId = 4;
    private const int _cleanupFailedEventId = 5;
    private const int _sourceNotDeletedEventId = 6;

    private readonly FakeLogger<SevenZipArchiver> _logger = new();
    private readonly BackupOptions _options = new();

    /// <summary>
    /// Creates the source folder with a file and a runner whose <c>a</c> command writes the temporary archive.
    /// </summary>
    public SevenZipArchiverTests()
    {
        _fileSystem.AddFile(@"C:\backup\owner\repo\README.md", new MockFileData("readme"));
        _runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(Run(call.Arg<ProcessRequest>(), 0)));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateAsync_Success_RunsCreateThenTestAndMovesArchive()
    {
        var result = await CreateArchiver().CreateAsync(Request(), CancellationToken);

        result.Path.ShouldBe(Path.Combine(_destination, _fileName));
        result.SizeBytes.ShouldBe(_archiveContent.Length);
        _fileSystem.AllFiles.Where(file => file.StartsWith(@"C:\backup\.tmp", StringComparison.Ordinal)).ShouldBeEmpty();
        _fileSystem.File.ReadAllText(result.Path).ShouldBe(_archiveContent);

        var calls = _runner.ReceivedCalls().Select(call => (ProcessRequest)call.GetArguments()[0]!).ToList();
        calls.Count.ShouldBe(2);
        calls.ShouldAllBe(request => request.FileName == _sevenZip && request.WorkingDirectory == @"C:\backup\owner");

        var temporaryPath = calls[0].Arguments[^2];
        temporaryPath.ShouldStartWith(@"C:\backup\.tmp\owner_repo_20261111-132030_0123456789.");
        temporaryPath.ShouldEndWith(".zip");
        calls[0].Arguments.ShouldBe(["a", "-tzip", "-mcu=on", "-bsp0", "-sccUTF-8", "-y", "--", temporaryPath, "repo"]);
        calls[1].Arguments.ShouldBe(["t", "-bsp0", "-sccUTF-8", "--", temporaryPath]);
    }

    [Fact]
    public async Task CreateAsync_SourceWithTrailingSeparator_ArchivesFolderName()
    {
        await CreateArchiver().CreateAsync(Request() with { SourceFolder = _source + @"\" }, CancellationToken);

        var create = (ProcessRequest)_runner.ReceivedCalls().First().GetArguments()[0]!;
        create.Arguments[^1].ShouldBe("repo");
        create.WorkingDirectory.ShouldBe(@"C:\backup\owner");
    }

    [Fact]
    public async Task CreateAsync_VerificationFails_LeavesNoFilesInArchiveOrTemporaryFolder()
    {
        StubExit(command: "t", exitCode: 2, "ERROR: CRC Failed");

        var exception = await Should.ThrowAsync<ArchiveCommandException>(() => CreateArchiver().CreateAsync(Request(), CancellationToken));

        exception.ExitCode.ShouldBe(2);
        exception.Command.ShouldStartWith("7z t");
        exception.Message.ShouldContain("ERROR: CRC Failed");
        AllArchiveFiles().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public async Task CreateAsync_CreateExitsNonZero_ThrowsWithCommandCodeAndErrorTail(int exitCode)
    {
        StubExit(command: "a", exitCode, "line 1", "line 2", "line 3", "line 4", "line 5", "line 6");

        var exception = await Should.ThrowAsync<ArchiveCommandException>(() => CreateArchiver().CreateAsync(Request(), CancellationToken));

        exception.ExitCode.ShouldBe(exitCode);
        exception.Command.ShouldStartWith("7z a ");
        exception.Message.ShouldContain($"exit code {exitCode}");
        exception.Message.ShouldContain("line 6");
        exception.Message.ShouldNotContain("line 1");
        AllArchiveFiles().ShouldBeEmpty();
        await _runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_VerifyArchiveOff_DoesNotRunTest()
    {
        _options.VerifyArchive = false;

        await CreateArchiver().CreateAsync(Request(), CancellationToken);

        var calls = _runner.ReceivedCalls().Select(call => (ProcessRequest)call.GetArguments()[0]!).ToList();
        calls.ShouldHaveSingleItem().Arguments[0].ShouldBe("a");
        _fileSystem.File.Exists(Path.Combine(_destination, _fileName)).ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_DifferentVolumes_CopiesRenamesAndDeletesSource()
    {
        var result = await CreateArchiver().CreateAsync(Request() with { DestinationFolder = @"D:\archives" }, CancellationToken);

        result.Path.ShouldBe(@"D:\archives\" + _fileName);
        _fileSystem.AllFiles.ShouldBe([@"C:\backup\owner\repo\README.md", result.Path]);
    }

    [Fact]
    public async Task CreateAsync_VolumeRootsDifferInCase_MovesWithoutCopy()
    {
        var result = await CreateArchiver().CreateAsync(Request() with { DestinationFolder = @"c:\archives" }, CancellationToken);

        _fileSystem.File.Exists(result.Path).ShouldBeTrue();
        var record = Records(_movingEventId).ShouldHaveSingleItem();
        record.GetStructuredStateValue("Destination").ShouldBe(result.Path);
        record.GetStructuredStateValue("SameVolume").ShouldBe(bool.TrueString);
    }

    [Theory]
    [InlineData(@"C:\archives")]
    [InlineData(@"D:\archives")]
    public async Task CreateAsync_MoveFails_LeavesNoFilesInArchiveOrTemporaryFolder(string destination)
    {
        _fileSystem.FailMove = true;

        await Should.ThrowAsync<IOException>(() => CreateArchiver().CreateAsync(Request() with { DestinationFolder = destination }, CancellationToken));

        AllArchiveFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_DifferentVolumesSourceNotDeleted_SucceedsWithWarning()
    {
        _fileSystem.FailDelete = true;

        var result = await CreateArchiver().CreateAsync(Request() with { DestinationFolder = @"D:\archives" }, CancellationToken);

        _fileSystem.File.Exists(result.Path).ShouldBeTrue();
        var record = Records(_sourceNotDeletedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldNotBeNull().ShouldStartWith(_temporary + @"\");
    }

    [Fact]
    public async Task CreateAsync_CleanupFails_StillThrowsOriginalError()
    {
        StubExit(command: "t", exitCode: 2, "bad archive");
        _fileSystem.FailDelete = true;

        await Should.ThrowAsync<ArchiveCommandException>(() => CreateArchiver().CreateAsync(Request(), CancellationToken));

        var records = Records(_cleanupFailedEventId);
        records.ShouldNotBeEmpty();
        records.ShouldAllBe(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task CreateAsync_CancelledWhileRunning_RemovesTemporaryFilesAndThrows()
    {
        _runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Run(call.Arg<ProcessRequest>(), 0);
            return Task.FromException<ProcessResult>(new OperationCanceledException());
        });

        await Should.ThrowAsync<OperationCanceledException>(() => CreateArchiver().CreateAsync(Request(), CancellationToken));

        AllArchiveFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_CancelledBeforeMove_RemovesTemporaryFilesAndThrows()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        _runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var result = Run(call.Arg<ProcessRequest>(), 0);
            source.Cancel();
            return Task.FromResult(result);
        });

        await Should.ThrowAsync<OperationCanceledException>(() => CreateArchiver().CreateAsync(Request(), source.Token));

        AllArchiveFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_DestinationFileExists_ThrowsAndKeepsExistingFile()
    {
        var existing = Path.Combine(_destination, _fileName);
        _fileSystem.AddFile(existing, new MockFileData("older"));

        await Should.ThrowAsync<IOException>(() => CreateArchiver().CreateAsync(Request(), CancellationToken));

        _fileSystem.File.ReadAllText(existing).ShouldBe("older");
        await _runner.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_SourceFolderMissing_ThrowsArgumentException()
    {
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => CreateArchiver().CreateAsync(Request() with { SourceFolder = @"C:\backup\owner\missing" }, CancellationToken));

        exception.Message.ShouldContain("does not exist");
        await _runner.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("SevenZipPath")]
    [InlineData("SourceFolder")]
    [InlineData("TemporaryFolder")]
    [InlineData("DestinationFolder")]
    public async Task CreateAsync_RelativePath_ThrowsArgumentException(string property)
    {
        var request = property switch
        {
            "SevenZipPath" => Request() with { SevenZipPath = "7z.exe" },
            "SourceFolder" => Request() with { SourceFolder = @"backup\repo" },
            "TemporaryFolder" => Request() with { TemporaryFolder = ".tmp" },
            _ => Request() with { DestinationFolder = "archives" },
        };

        var exception = await Should.ThrowAsync<ArgumentException>(() => CreateArchiver().CreateAsync(request, CancellationToken));

        exception.Message.ShouldContain(property);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"sub\a.zip")]
    [InlineData(@"C:\a.zip")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("CON.zip")]
    [InlineData("a*.zip")]
    [InlineData("a?.zip")]
    public async Task CreateAsync_UnsafeFileName_ThrowsArgumentExceptionWithoutStartingProcess(string fileName)
    {
        await Should.ThrowAsync<ArgumentException>(() => CreateArchiver().CreateAsync(Request() with { FileName = fileName }, CancellationToken));

        await _runner.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    private const string _archiveContent = "zip bytes";

    /// <summary>
    /// Makes the next calls of the command exit with a non-zero code and the given standard error.
    /// </summary>
    /// <param name="command">The 7-Zip command, <c>a</c> or <c>t</c>.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="error">The standard error lines.</param>
    private void StubExit(string command, int exitCode, params string[] error) =>
        _runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<ProcessRequest>();
            return Task.FromResult(request.Arguments[0] == command ? Run(request, exitCode, error) : Run(request, 0));
        });

    /// <summary>
    /// Plays 7-Zip: writes the temporary archive for <c>a</c> and returns the result.
    /// </summary>
    /// <param name="request">The process request.</param>
    /// <param name="exitCode">The exit code to report.</param>
    /// <param name="error">The standard error lines.</param>
    /// <returns>The process result.</returns>
    private ProcessResult Run(ProcessRequest request, int exitCode, params string[] error)
    {
        if (request.Arguments[0] == "a")
        {
            _fileSystem.AddFile(request.Arguments[^2], new MockFileData(_archiveContent));
        }

        return new ProcessResult(exitCode, TimeSpan.Zero, [], error);
    }

    /// <summary>
    /// Lists the files in the temporary and archive folders.
    /// </summary>
    /// <returns>The paths of the files.</returns>
    private List<string> AllArchiveFiles() =>
        [.. _fileSystem.AllFiles.Where(file => !file.StartsWith(_source, StringComparison.Ordinal))];

    private static ArchiveRequest Request() => new()
    {
        SevenZipPath = _sevenZip,
        SourceFolder = _source,
        TemporaryFolder = _temporary,
        DestinationFolder = _destination,
        FileName = _fileName,
    };

    private SevenZipArchiver CreateArchiver() => new(_runner, _fileSystem, Options.Create(_options), _logger);

    /// <summary>
    /// Returns the log records with the event id <paramref name="eventId"/>, in the order they were written.
    /// </summary>
    /// <param name="eventId">The event id of the <see cref="LoggerMessageAttribute"/>.</param>
    /// <returns>The records.</returns>
    private List<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
