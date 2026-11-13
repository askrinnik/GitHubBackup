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

    private readonly BackupRunResult _result = new([], [], []);

    private readonly BackupRunService _service;

    /// <summary>
    /// Creates the service over an orchestrator that returns <see cref="_result"/>.
    /// </summary>
    public BackupRunServiceTests()
    {
        _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(_result);
        _service = new(_orchestrator, _runContext, _decisionHandler);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_BuildsUiRequestWithFilterAndDryRun(bool dryRun)
    {
        var filter = BackupRunFilter.ForAccount("octo");

        var result = await _service.RunAsync(filter, dryRun, TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(_result);
        await _orchestrator.Received(1).RunAsync(
            Arg.Is<BackupRunRequest>(request =>
                request.Filter == filter
                && request.DryRun == dryRun
                && request.Mode == RunMode.Ui
                && request.DecisionHandler == _decisionHandler),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_BeginsNewRunBeforeOrchestrator()
    {
        await _service.RunAsync(BackupRunFilter.All, false, TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _ = _runContext.BeginRun();
            _ = _orchestrator.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>());
        });
    }
}
