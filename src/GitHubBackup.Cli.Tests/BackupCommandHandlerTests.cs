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
    private readonly AutomaticDiscoveryDecisionHandler _automaticHandler = new();
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

        var exitCode = await CreateHandler().RunAsync(BackupCommandOptions.Default, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(expected);
    }

    [Fact]
    public async Task RunAsync_Completed_WritesSummaryWithLogOfLocalDate()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(new BackupRunResult([], [], []));

        await CreateHandler().RunAsync(BackupCommandOptions.Default, TestContext.Current.CancellationToken);

        _output.Output.ShouldContain(@"Log: C:\app\logs\githubbackup-20261112.log");
    }

    [Fact]
    public async Task RunAsync_RunStopped_ReturnsCriticalAndWritesErrors()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(BackupRunResult.Stopped(["git was not found."], []));

        var exitCode = await CreateHandler().RunAsync(BackupCommandOptions.Default, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldStartWith("Error: git was not found.");
    }

    [Fact]
    public async Task RunAsync_Silent_RunsAllRepositoriesWithAutomaticDecisions()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(new BackupRunResult([], [], []));

        await CreateHandler().RunAsync(BackupCommandOptions.Default with { Silent = true }, TestContext.Current.CancellationToken);

        await _orchestrator.Received(1).RunAsync(
            new BackupRunRequest(BackupRunFilter.All, _automaticHandler), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_NotSilent_RunsAllRepositoriesWithConsoleDecisions()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(new BackupRunResult([], [], []));
        var handler = CreateHandler(out var consoleHandler);

        await handler.RunAsync(BackupCommandOptions.Default, TestContext.Current.CancellationToken);

        await _orchestrator.Received(1).RunAsync(
            new BackupRunRequest(BackupRunFilter.All, consoleHandler), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_RedirectedInputNotSilent_ReturnsCriticalWithHintWithoutRunningOrchestrator(bool withFilter)
    {
        var options = BackupCommandOptions.Default;
        if (withFilter)
        {
            options = options with { Filter = BackupRunFilter.ForRepository("octocat", "hello") };
        }

        var exitCode = await CreateHandler(isInputRedirected: true).RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldStartWith("Error: " + BackupCommandHandler.RedirectedInputMessage);
        BackupCommandHandler.RedirectedInputMessage.ShouldContain("--silent");
        await _orchestrator.DidNotReceiveWithAnyArgs().RunAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_RedirectedInputSilent_RunsOrchestrator()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(new BackupRunResult([], [], []));

        var exitCode = await CreateHandler(isInputRedirected: true).RunAsync(
            BackupCommandOptions.Default with { Silent = true }, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Success);
        await _orchestrator.Received(1).RunAsync(
            new BackupRunRequest(BackupRunFilter.All, _automaticHandler), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_DryRunWithRedirectedInput_ReportsDryRunNotRedirectedInput()
    {
        var exitCode = await CreateHandler(isInputRedirected: true).RunAsync(
            BackupCommandOptions.Default with { DryRun = true }, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldStartWith("Error: " + BackupCommandHandler.DryRunNotImplementedMessage);
    }

    [Fact]
    public async Task RunAsync_Filter_IsPassedToOrchestrator()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(new BackupRunResult([], [], []));
        var filter = BackupRunFilter.ForRepository("octocat", "hello");

        await CreateHandler().RunAsync(new(filter, DryRun: false, Silent: true), TestContext.Current.CancellationToken);

        await _orchestrator.Received(1).RunAsync(
            new BackupRunRequest(filter, _automaticHandler), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_DryRun_ReturnsCriticalWithoutRunningOrchestrator()
    {
        var exitCode = await CreateHandler().RunAsync(
            BackupCommandOptions.Default with { DryRun = true }, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldStartWith("Error: " + BackupCommandHandler.DryRunNotImplementedMessage);
        await _orchestrator.DidNotReceiveWithAnyArgs().RunAsync(default!, TestContext.Current.CancellationToken);
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

        var exitCode = await CreateHandler().RunAsync(BackupCommandOptions.Default, TestContext.Current.CancellationToken);

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
    /// <param name="isInputRedirected">Whether the console reports redirected input.</param>
    private BackupCommandHandler CreateHandler(bool isInputRedirected = false) => CreateHandler(out _, isInputRedirected);

    /// <summary>
    /// Creates the handler and exposes the console decision handler it uses.
    /// </summary>
    /// <param name="consoleHandler">The console decision handler.</param>
    /// <param name="isInputRedirected">Whether the console reports redirected input.</param>
    /// <returns>The handler.</returns>
    private BackupCommandHandler CreateHandler(out ConsoleDiscoveryDecisionHandler consoleHandler, bool isInputRedirected = false)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRoot);
        var console = new CliConsole(_output, _error, isInputRedirected);
        consoleHandler = new(console);
        return new(_orchestrator, _automaticHandler, consoleHandler, console, new(console), environment, _timeProvider);
    }
}
