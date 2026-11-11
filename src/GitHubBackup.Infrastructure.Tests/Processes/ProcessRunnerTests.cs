using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Processes;

/// <summary>
/// Tests the checks <see cref="ProcessRunner"/> makes before it starts a process.
/// </summary>
public sealed class ProcessRunnerTests
{
    private const string _toolPath = @"C:\tools\tool.exe";
    private const string _workingDirectory = @"C:\work";

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<ProcessRunner> _logger = new();
    private readonly ProcessRunner _runner;

    /// <summary>
    /// Creates the runner over a file system that holds the tool and the working directory.
    /// </summary>
    public ProcessRunnerTests()
    {
        _fileSystem.AddFile(_toolPath, new(string.Empty));
        _fileSystem.AddDirectory(_workingDirectory);
        _runner = new(new SecretMasker(), _fileSystem, Substitute.For<TimeProvider>(), _logger);
    }

    [Theory]
    [InlineData("tool.exe", _workingDirectory)]
    [InlineData(@"tools\tool.exe", _workingDirectory)]
    [InlineData(_toolPath, "work")]
    [InlineData(_toolPath, @"\work")]
    public async Task RunAsync_PathNotFullyQualified_ThrowsArgumentException(string fileName, string workingDirectory)
    {
        var request = new ProcessRequest { FileName = fileName, WorkingDirectory = workingDirectory };

        await Should.ThrowAsync<ArgumentException>(() => _runner.RunAsync(request, TestContext.Current.CancellationToken));

        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_ThrowsProcessStartExceptionNamingPath()
    {
        var request = new ProcessRequest { FileName = @"C:\tools\missing.exe", WorkingDirectory = _workingDirectory };

        var exception = await Should.ThrowAsync<ProcessStartException>(
            () => _runner.RunAsync(request, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(@"C:\tools\missing.exe");
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_MissingWorkingDirectory_ThrowsProcessStartExceptionNamingPath()
    {
        var request = new ProcessRequest { FileName = _toolPath, WorkingDirectory = @"C:\missing" };

        var exception = await Should.ThrowAsync<ProcessStartException>(
            () => _runner.RunAsync(request, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(@"C:\missing");
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeStart_ThrowsWithoutStarting()
    {
        var request = new ProcessRequest { FileName = _toolPath, WorkingDirectory = _workingDirectory };

        await Should.ThrowAsync<OperationCanceledException>(
            () => _runner.RunAsync(request, new CancellationToken(canceled: true)));

        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }
}
