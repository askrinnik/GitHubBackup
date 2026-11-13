using GitHubBackup.App.Discovery;
using GitHubBackup.App.Runs;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Runs;
using NSubstitute;

namespace GitHubBackup.App.Tests.Runs;

/// <summary>
/// Tests <see cref="BackupRunService"/>.
/// </summary>
public sealed class BackupRunServiceTests
{
    private readonly IBackupOrchestrator _orchestrator = Substitute.For<IBackupOrchestrator>();

    private readonly IRunContext _runContext = Substitute.For<IRunContext>();

    private readonly IDiscoveryDecisionHandler _decisionHandler = Substitute.For<IDiscoveryDecisionHandler>();

    private readonly IDiscoveryDecisionHandlerFactory _handlerFactory = Substitute.For<IDiscoveryDecisionHandlerFactory>();

    private readonly BackupRunResult _result = new([], [], []);

    private readonly BackupRunService _service;

    /// <summary>The cancellation callback the service passed to the factory, or <see langword="null"/>.</summary>
    private Action? _cancelRun;

    /// <summary>
    /// Creates the service over an orchestrator that returns <see cref="_result"/> and a factory that returns
    /// <see cref="_decisionHandler"/> and records the cancellation callback.
    /// </summary>
    public BackupRunServiceTests()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(_result);
        _handlerFactory.Create(Arg.Do<Action>(cancelRun => _cancelRun = cancelRun)).Returns(_decisionHandler);
        _service = new(_orchestrator, _runContext, _handlerFactory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_BuildsUiRequestWithFilterDryRunAndHandlerFromFactory(bool dryRun)
    {
        var filter = BackupRunFilter.ForAccount("octo");

        var result = await _service.RunAsync(filter, dryRun, null, TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(_result);
        await _orchestrator.Received(1).RunAsync(
            Arg.Is<BackupRunRequest>(request =>
                request.Filter == filter
                && request.DryRun == dryRun
                && request.Mode == RunMode.Ui
                && request.DecisionHandler == _decisionHandler
                && request.Progress == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_EachRun_CreatesNewHandler()
    {
        await _service.RunAsync(BackupRunFilter.All, false, null, TestContext.Current.CancellationToken);
        await _service.RunAsync(BackupRunFilter.All, false, null, TestContext.Current.CancellationToken);

        _handlerFactory.ReceivedWithAnyArgs(2).Create(default!);
    }

    [Fact]
    public async Task RunAsync_Progress_PassesProgressToRequest()
    {
        var progress = Substitute.For<IProgress<BackupProgress>>();

        await _service.RunAsync(BackupRunFilter.All, false, progress, TestContext.Current.CancellationToken);

        await _orchestrator.Received(1).RunAsync(
            Arg.Is<BackupRunRequest>(request => request.Progress == progress), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TokenCancelledBeforeStart_StillCallsOrchestratorWithCancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await _service.RunAsync(BackupRunFilter.All, false, null, cancellation.Token);

        result.ShouldBeSameAs(_result);
        await _orchestrator.Received(1).RunAsync(
            Arg.Any<BackupRunRequest>(), Arg.Is<CancellationToken>(token => token.IsCancellationRequested));
    }

    [Fact]
    public async Task RunAsync_CallerCancelsDuringRun_CancelsOrchestratorToken()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelledInRun = false;
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await cancellation.CancelAsync();
            cancelledInRun = call.Arg<CancellationToken>().IsCancellationRequested;
            return _result;
        });

        await _service.RunAsync(BackupRunFilter.All, false, null, cancellation.Token);

        cancelledInRun.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_HandlerCancelsRun_CancelsOrchestratorTokenButNotCallerToken()
    {
        var cancelledInRun = false;
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _cancelRun.ShouldNotBeNull().Invoke();
            cancelledInRun = call.Arg<CancellationToken>().IsCancellationRequested;
            return _result;
        });

        var result = await _service.RunAsync(BackupRunFilter.All, false, null, TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(_result);
        cancelledInRun.ShouldBeTrue();
        TestContext.Current.CancellationToken.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_BeginsNewRunBeforeOrchestrator()
    {
        await _service.RunAsync(BackupRunFilter.All, false, null, TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _ = _runContext.BeginRun();
            _ = _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>());
        });
    }
}
