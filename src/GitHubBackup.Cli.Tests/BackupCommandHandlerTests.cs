using GitHubBackup.Core;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Spectre.Console.Testing;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests <see cref="BackupCommandHandler"/> with a substitute <see cref="IBackupOrchestrator"/>.
/// </summary>
public sealed class BackupCommandHandlerTests : IDisposable
{
    private const string _contentRoot = @"C:\app";

    private readonly IBackupOrchestrator _orchestrator = Substitute.For<IBackupOrchestrator>();
    private readonly AutomaticDiscoveryDecisionHandler _decisionHandler = new();
    private readonly TestConsole _output = TestCliConsole.CreateOutput();
    private readonly StringWriter _error = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 11, 23, 30, 0, TimeSpan.Zero));

    /// <summary>
    /// Sets the local time zone of the clock to UTC+2, where the fixed instant falls on the next day.
    /// </summary>
    public BackupCommandHandlerTests()
    {
        _timeProvider.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2"));
    }

    public static TheoryData<BackupItemStatus, ExitCode> ExitCodes => new()
    {
        { BackupItemStatus.Archived, ExitCode.Success },
        { BackupItemStatus.UpToDate, ExitCode.Success },
        { BackupItemStatus.Warning, ExitCode.Success },
        { BackupItemStatus.Skipped, ExitCode.Success },
        { BackupItemStatus.Failed, ExitCode.RepositoriesFailed },
    };

    [Theory]
    [MemberData(nameof(ExitCodes))]
    public async Task RunAsync_OneRepository_ReturnsExitCodeOfResult(BackupItemStatus status, ExitCode expected)
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BackupRunResult([new("octocat/hello", status, null, TimeSpan.Zero, null)], [], []));

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(expected);
    }

    [Fact]
    public async Task RunAsync_Completed_WritesSummaryWithLogOfLocalDate()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(new BackupRunResult([], [], []));

        await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        _output.Output.ShouldContain(@"Log: C:\app\logs\githubbackup-20261112.log");
    }

    [Fact]
    public async Task RunAsync_RunStopped_ReturnsCriticalAndWritesErrors()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(BackupRunResult.Stopped(["git was not found."], []));

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldStartWith("Error: git was not found.");
    }

    [Fact]
    public async Task RunAsync_Called_RunsAllRepositoriesWithAutomaticDecisions()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(new BackupRunResult([], [], []));

        await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        await _orchestrator.Received(1).RunAsync(
            new BackupRunRequest(BackupRunFilter.All, _decisionHandler), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_DiscoveryEvents_ReturnsDiscoveryEvents()
    {
        var discovery = new DiscoveryApplyResult(["octocat/new"], [], [], [], [], [], ConfigSaved: true);
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(
            new BackupRunResult([new("octocat/new", BackupItemStatus.Archived, null, TimeSpan.Zero, null)], [], [])
            {
                Discovery = discovery,
            });

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.DiscoveryEvents);
    }

    /// <summary>
    /// Disposes the outputs.
    /// </summary>
    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    /// <summary>
    /// Creates the handler over the substitute orchestrator, the test outputs and the fixed clock.
    /// </summary>
    /// <returns>The handler.</returns>
    private BackupCommandHandler CreateHandler()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRoot);
        return new(_orchestrator, _decisionHandler, new(new(_output, _error)), environment, _timeProvider);
    }
}
