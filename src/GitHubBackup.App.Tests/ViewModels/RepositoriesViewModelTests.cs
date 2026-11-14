using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Navigation;
using GitHubBackup.App.Runs;
using GitHubBackup.App.Tests.Logging;
using GitHubBackup.App.Tests.Runs;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="RepositoriesViewModel"/>: reading the tree, the availability of the backup commands, the runs, their
/// summary, cancellation and closing the window during a run.
/// </summary>
public sealed class RepositoriesViewModelTests
{
    private static readonly DateTimeOffset _archivedAt = new(2026, 11, 5, 9, 7, 0, TimeSpan.FromHours(2));

    private static readonly RepositoryOverview _overview = new(
        [
            new("octo", "https://github.com/octo",
            [
                new("octo", "tool", "octo", RepositoryOverviewStatus.Active, _archivedAt, null) { RepositoryId = 1, ArchiveFolder = @"C:\backup" },
                new("octo", "old", "octo", RepositoryOverviewStatus.Unavailable, null, null) { RepositoryId = 1, ArchiveFolder = @"C:\backup" },
                new("octo", "skip", "octo", RepositoryOverviewStatus.Excluded, null, "The state file is not valid JSON.") { RepositoryId = 1, ArchiveFolder = @"C:\backup" },
            ]),
            new("empty", "https://github.com/empty", []),
        ],
        [new("other", "lib", null, RepositoryOverviewStatus.Active, null, null) { RepositoryId = 1, ArchiveFolder = @"C:\backup" }]);

    private readonly IRepositoryOverviewProvider _provider = Substitute.For<IRepositoryOverviewProvider>();

    private readonly IBackupRunService _runService = Substitute.For<IBackupRunService>();

    private const int _backupRunFailedEventId = 4;

    private const int _treeLoadFailedEventId = 5;

    private const int _backupConfigMissingEventId = 13;

    private const string _configPath = @"C:\app\backup-config.json";

    private const string _token = "ghp_AppTestTokenValue0123456789abcdefABC";

    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private readonly ManualUiDispatcher _dispatcher = new();

    private readonly FakeLiveLogFeed _logFeed = new();

    private const string _logPath = @"C:\app\logs\githubbackup-20261113.log";

    private readonly FakeLogger<RepositoriesViewModel> _logger = new();

    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    private readonly RepositoriesViewModel _viewModel;

    /// <summary>
    /// Creates the ViewModel over a provider that returns <see cref="_overview"/> and a run that succeeds.
    /// </summary>
    public RepositoriesViewModelTests()
    {
        _provider.LoadAsync(Arg.Any<CancellationToken>()).Returns(_overview);
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupRunResult([], [], []));
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(@"C:\app");
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
        timeProvider.SetLocalTimeZone(TimeZoneInfo.Utc);
        _viewModel = new(_provider, _runService, _dispatcher, _logFeed, _dialogs, new SecretMasker(), environment, timeProvider, _navigation, _logger);
    }

    [Fact]
    public async Task Refresh_AccountsAndStandalone_BuildsAccountNodesThenGroup()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.Nodes.Select(node => node.DisplayName).ShouldBe(["octo", "empty", RepositoryGroupNodeViewModel.GroupName]);
        _viewModel.Nodes.ShouldAllBe(node => node.IsExpanded);
        _viewModel.Nodes[0].Children.Select(node => node.DisplayName).ShouldBe(["tool", "old", "skip"]);
        _viewModel.Nodes[1].Children.ShouldBeEmpty();
        _viewModel.Nodes[2].Children.Select(node => node.DisplayName).ShouldBe(["other/lib"]);
        _viewModel.LoadError.ShouldBeNull();
        _viewModel.HasLoadError.ShouldBeFalse();
        _viewModel.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_RepositoryNodes_ShowStatusAndLastArchiveText()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        var tool = (RepositoryNodeViewModel)FindNode("octo/tool");
        var old = (RepositoryNodeViewModel)FindNode("octo/old");
        var skip = (RepositoryNodeViewModel)FindNode("octo/skip");
        tool.Status.ShouldBe(RepositoryOverviewStatus.Active);
        tool.LastArchiveText.ShouldBe("2026-11-05 09:07");
        old.Status.ShouldBe(RepositoryOverviewStatus.Unavailable);
        old.LastArchiveText.ShouldBe(RepositoryNodeViewModel.NeverText);
        skip.Status.ShouldBe(RepositoryOverviewStatus.Excluded);
        skip.LastArchiveText.ShouldBe(RepositoryNodeViewModel.UnknownText);
        skip.StateError.ShouldBe("The state file is not valid JSON.");
    }

    [Fact]
    public async Task Refresh_OnlyStandaloneRepositories_ShowsOnlyGroup()
    {
        _provider.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(new RepositoryOverview([], [new("other", "lib", null, RepositoryOverviewStatus.Active, null, null) { RepositoryId = 1, ArchiveFolder = @"C:\backup" }]));

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.Nodes.ShouldHaveSingleItem().ShouldBeOfType<RepositoryGroupNodeViewModel>();
    }

    [Fact]
    public async Task Refresh_EmptyConfig_SetsIsEmpty()
    {
        _provider.LoadAsync(Arg.Any<CancellationToken>()).Returns(new RepositoryOverview([], []));

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.Nodes.ShouldBeEmpty();
        _viewModel.IsEmpty.ShouldBeTrue();
        _viewModel.HasLoadError.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_ConfigError_SetsLoadErrorAndClearsTree()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        var error = new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set."]);
        _provider.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(error);

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.Nodes.ShouldBeEmpty();
        _viewModel.SelectedNode.ShouldBeNull();
        _viewModel.LoadError.ShouldBe(error.Message);
        _viewModel.HasLoadError.ShouldBeTrue();
        _viewModel.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_ErrorThenSuccess_ClearsLoadError()
    {
        _provider.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set."]),
            _ => _overview);
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.LoadError.ShouldBeNull();
        _viewModel.Nodes.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Refresh_ConfigNotFound_SetsConfigMissingWithoutLoadError()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        _provider.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new BackupConfigNotFoundException(_configPath));

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.IsConfigMissing.ShouldBeTrue();
        _viewModel.LoadError.ShouldBeNull();
        _viewModel.HasLoadError.ShouldBeFalse();
        _viewModel.IsEmpty.ShouldBeFalse();
        _viewModel.Nodes.ShouldBeEmpty();
        _viewModel.SelectedNode.ShouldBeNull();
        _logger.Collector.GetSnapshot().ShouldNotContain(record => record.Id.Id == _treeLoadFailedEventId);
        var record = _logger.Collector.GetSnapshot().Where(entry => entry.Id.Id == _backupConfigMissingEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Path").ShouldBe(_configPath);
        record.Exception.ShouldBeNull();
    }

    [Theory]
    [InlineData(BackupScope.All)]
    [InlineData(BackupScope.Account)]
    [InlineData(BackupScope.Repository)]
    public async Task Refresh_ConfigNotFound_CannotStartAnyScope(BackupScope scope)
    {
        _provider.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new BackupConfigNotFoundException(_configPath));

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.StartBackupCommand.CanExecute(scope).ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_ConfigNotFoundThenFound_ClearsConfigMissingAndEnablesBackup()
    {
        _provider.LoadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new BackupConfigNotFoundException(_configPath),
            _ => _overview);
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        var raised = 0;
        _viewModel.StartBackupCommand.CanExecuteChanged += (_, _) => raised++;

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.IsConfigMissing.ShouldBeFalse();
        _viewModel.Nodes.Count.ShouldBe(3);
        _viewModel.StartBackupCommand.CanExecute(BackupScope.All).ShouldBeTrue();
        raised.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Refresh_OtherConfigError_DoesNotSetConfigMissing()
    {
        _provider.LoadAsync(Arg.Any<CancellationToken>()).Returns<RepositoryOverview>(
            _ => throw new BackupConfigNotFoundException(_configPath),
            _ => throw new BackupConfigException(_configPath, ["backupRoot must be set."]));
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.IsConfigMissing.ShouldBeFalse();
        _viewModel.HasLoadError.ShouldBeTrue();
    }

    [Fact]
    public async Task OpenSettings_ConfigMissing_NavigatesToSettings()
    {
        _provider.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new BackupConfigNotFoundException(_configPath));
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.OpenSettingsCommand.CanExecute(null).ShouldBeTrue();
        _viewModel.OpenSettingsCommand.Execute(null);

        _navigation.Received(1).NavigateTo<SettingsViewModel>();
    }

    [Theory]
    [InlineData(null, BackupScope.All, true)]
    [InlineData(null, BackupScope.Account, false)]
    [InlineData(null, BackupScope.Repository, false)]
    [InlineData("octo", BackupScope.All, true)]
    [InlineData("octo", BackupScope.Account, true)]
    [InlineData("octo", BackupScope.Repository, false)]
    [InlineData("octo/tool", BackupScope.Account, true)]
    [InlineData("octo/tool", BackupScope.Repository, true)]
    [InlineData("octo/old", BackupScope.Account, true)]
    [InlineData("octo/old", BackupScope.Repository, false)]
    [InlineData("octo/skip", BackupScope.Repository, false)]
    [InlineData("group", BackupScope.Account, false)]
    [InlineData("group", BackupScope.Repository, false)]
    [InlineData("other/lib", BackupScope.Account, false)]
    [InlineData("other/lib", BackupScope.Repository, true)]
    public async Task CanStartBackup_BySelection_MatchesScope(string? selection, BackupScope scope, bool expected)
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        if (selection is not null)
        {
            FindNode(selection).IsSelected = true;
        }

        _viewModel.StartBackupCommand.CanExecute(scope).ShouldBe(expected);
    }

    [Fact]
    public async Task SelectNode_ChangesSelection_RaisesCanExecuteChanged()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        var raised = 0;
        _viewModel.StartBackupCommand.CanExecuteChanged += (_, _) => raised++;

        FindNode("octo/tool").IsSelected = true;
        FindNode("octo/tool").IsSelected = false;

        raised.ShouldBe(2);
        _viewModel.SelectedNode.ShouldBeNull();
    }

    [Fact]
    public async Task StartBackup_WhileRunning_CannotExecuteAnyScope()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        FindNode("octo/tool").IsSelected = true;
        var run = new TaskCompletionSource<BackupRunResult>();
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>()).Returns(run.Task);

        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _viewModel.IsRunning.ShouldBeTrue();
        _viewModel.IsIdle.ShouldBeFalse();
        _viewModel.StartBackupCommand.CanExecute(BackupScope.All).ShouldBeFalse();
        _viewModel.StartBackupCommand.CanExecute(BackupScope.Account).ShouldBeFalse();
        _viewModel.StartBackupCommand.CanExecute(BackupScope.Repository).ShouldBeFalse();
        run.SetResult(new([], [], []));
        await execution;
        _viewModel.IsRunning.ShouldBeFalse();
        _viewModel.StartBackupCommand.CanExecute(BackupScope.All).ShouldBeTrue();
    }

    [Fact]
    public void OpenSettings_Idle_NavigatesToSettings()
    {
        _viewModel.OpenSettingsCommand.CanExecute(null).ShouldBeTrue();

        _viewModel.OpenSettingsCommand.Execute(null);

        _navigation.Received(1).NavigateTo<SettingsViewModel>();
    }

    [Fact]
    public async Task OpenSettings_Running_CannotExecute()
    {
        var run = new TaskCompletionSource<BackupRunResult>();
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>()).Returns(run.Task);
        var raised = 0;
        _viewModel.OpenSettingsCommand.CanExecuteChanged += (_, _) => raised++;

        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _viewModel.OpenSettingsCommand.CanExecute(null).ShouldBeFalse();
        raised.ShouldBe(1);
        run.SetResult(new([], [], []));
        await execution;
        _viewModel.OpenSettingsCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void OpenHistory_Idle_NavigatesToHistory()
    {
        _viewModel.OpenHistoryCommand.CanExecute(null).ShouldBeTrue();

        _viewModel.OpenHistoryCommand.Execute(null);

        _navigation.Received(1).NavigateTo<HistoryViewModel>();
    }

    [Fact]
    public async Task OpenHistory_Running_CannotExecute()
    {
        var run = new TaskCompletionSource<BackupRunResult>();
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>()).Returns(run.Task);
        var raised = 0;
        _viewModel.OpenHistoryCommand.CanExecuteChanged += (_, _) => raised++;

        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _viewModel.OpenHistoryCommand.CanExecute(null).ShouldBeFalse();
        raised.ShouldBe(1);
        run.SetResult(new([], [], []));
        await execution;
        _viewModel.OpenHistoryCommand.CanExecute(null).ShouldBeTrue();
    }

    [Theory]
    [InlineData(BackupScope.All, null, null, null)]
    [InlineData(BackupScope.Account, "octo", null, null)]
    [InlineData(BackupScope.Repository, null, "octo", "tool")]
    public async Task StartBackup_Scope_PassesFilter(
        BackupScope scope, string? accountLogin, string? repositoryOwner, string? repositoryName)
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        FindNode("octo/tool").IsSelected = true;

        await _viewModel.StartBackupCommand.ExecuteAsync(scope);

        await _runService.Received(1).RunAsync(
            Arg.Is<BackupRunFilter>(filter =>
                filter.AccountLogin == accountLogin
                && filter.RepositoryOwner == repositoryOwner
                && filter.RepositoryName == repositoryName),
            false,
            Arg.Any<IProgress<BackupProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartBackup_DryRunToggle_PassesDryRun(bool dryRun)
    {
        _viewModel.IsDryRun = dryRun;

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        await _runService.Received(1).RunAsync(BackupRunFilter.All, dryRun, Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartBackup_RereadsConfigBeforeRun()
    {
        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        Received.InOrder(() =>
        {
            _ = _provider.LoadAsync(Arg.Any<CancellationToken>());
            _ = _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>());
            _ = _provider.LoadAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task StartBackup_RereadFails_StillStartsRun()
    {
        _provider.LoadAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set."]));

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        await _runService.Received(1).RunAsync(BackupRunFilter.All, false, Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>());
        _viewModel.HasLoadError.ShouldBeTrue();
    }

    [Fact]
    public async Task StartBackup_CriticalErrors_ShowsErrorsInSummaryWithoutDialog()
    {
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(BackupRunResult.Stopped([BackupOrchestrator.RunLockHeldMessage, "Git was not found."], []));

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        var summary = _viewModel.Summary.ShouldNotBeNull();
        summary.Errors.ShouldBe([BackupOrchestrator.RunLockHeldMessage, "Git was not found."]);
        summary.HasItems.ShouldBeFalse();
        summary.LogPath.ShouldBe(_logPath);
        _viewModel.HasSummary.ShouldBeTrue();
        _dialogs.DidNotReceiveWithAnyArgs().ShowError(default!, default!);
        _viewModel.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task StartBackup_HistoryWarning_ShowsWarningInSummaryWithoutDialog()
    {
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupRunResult([], [], []) { HistoryWarning = "The run was not recorded in the run history." });

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _viewModel.Summary.ShouldNotBeNull().Warnings.ShouldBe(["The run was not recorded in the run history."]);
        _dialogs.DidNotReceiveWithAnyArgs().ShowError(default!, default!);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, BackupRunSummary.DryRunHeader)]
    public async Task StartBackup_Finished_ShowsSummaryWithLogPath(bool dryRun, string? header)
    {
        _viewModel.IsDryRun = dryRun;
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupRunResult(
                [new("octo/tool", BackupItemStatus.Failed, null, TimeSpan.Zero, "The clone failed.")], [], [])
            { IsDryRun = dryRun });

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        var summary = _viewModel.Summary.ShouldNotBeNull();
        summary.Header.ShouldBe(header);
        summary.Rows.ShouldHaveSingleItem().Error.ShouldBe("The clone failed.");
        summary.LogPath.ShouldBe(_logPath);
        _dialogs.DidNotReceiveWithAnyArgs().ShowError(default!, default!);
    }

    [Fact]
    public async Task StartBackup_NoItems_ShowsNoRepositoriesLine()
    {
        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _viewModel.Summary.ShouldNotBeNull().NoRepositoriesLine.ShouldBe(BackupRunSummary.NoRepositoriesLine);
    }

    [Fact]
    public async Task StartBackup_NewRun_ClearsPreviousSummaryWhileRunning()
    {
        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        _viewModel.Summary.ShouldNotBeNull();
        var run = new TaskCompletionSource<BackupRunResult>();
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>()).Returns(run.Task);

        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _viewModel.Summary.ShouldBeNull();
        _viewModel.HasSummary.ShouldBeFalse();
        run.SetResult(new([], [], []));
        await execution;
        _viewModel.Summary.ShouldNotBeNull();
    }

    [Fact]
    public async Task StartBackup_RunnerThrows_ShowsErrorAndResetsIsRunning()
    {
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Something broke."));

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _dialogs.Received(1).ShowError(RepositoriesViewModel.RunFailedTitle, "Something broke.");
        _viewModel.IsRunning.ShouldBeFalse();
        _viewModel.Summary.ShouldBeNull();
    }

    [Fact]
    public async Task StartBackup_RunnerThrowsWithToken_MasksTokenInDialog()
    {
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException($"Request failed with token {_token}."));

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _dialogs.Received(1).ShowError(
            RepositoriesViewModel.RunFailedTitle, $"Request failed with token {SecretMasker.Replacement}.");
    }

    [Fact]
    public async Task StartBackup_RunnerThrows_LogsErrorWithException()
    {
        var error = new InvalidOperationException("Something broke.");
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>()).ThrowsAsync(error);

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        var record = _logger.Collector.GetSnapshot().Single(entry => entry.Id.Id == _backupRunFailedEventId);
        record.Level.ShouldBe(LogLevel.Error);
        record.Exception.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task Refresh_ErrorWithToken_MasksTokenInLoadErrorAndLogsWarning()
    {
        var error = new BackupConfigException(@"C:\app\backup-config.json", [$"The value {_token} is not valid."]);
        _provider.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(error);

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.LoadError.ShouldNotBeNull();
        _viewModel.LoadError.ShouldNotContain(_token);
        _viewModel.LoadError.ShouldContain(SecretMasker.Replacement);
        var record = _logger.Collector.GetSnapshot().Single(entry => entry.Id.Id == _treeLoadFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.Exception.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task StartBackup_RunnerCancelledBeforeLock_ShowsCancelledSummaryWithoutDialog()
    {
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        var summary = _viewModel.Summary.ShouldNotBeNull();
        summary.CancelledLine.ShouldBe(BackupRunSummary.CancelledLine);
        summary.LogPath.ShouldBe(_logPath);
        _dialogs.DidNotReceiveWithAnyArgs().ShowError(default!, default!);
        _viewModel.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void StartBackupCancel_NoRun_CannotExecute() =>
        _viewModel.StartBackupCancelCommand.CanExecute(null).ShouldBeFalse();

    [Fact]
    public async Task StartBackupCancel_DuringRun_CancelsTokenAndShowsCancelledSummary()
    {
        var token = UseRunThatEndsOnCancellation();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        _viewModel.StartBackupCancelCommand.CanExecute(null).ShouldBeTrue();

        _viewModel.StartBackupCancelCommand.Execute(null);

        _viewModel.StartBackupCommand.IsCancellationRequested.ShouldBeTrue();
        _viewModel.StartBackupCancelCommand.CanExecute(null).ShouldBeFalse();
        await execution;
        token().IsCancellationRequested.ShouldBeTrue();
        var summary = _viewModel.Summary.ShouldNotBeNull();
        summary.Rows.Select(row => row.Status).ShouldBe(["Cancelled", "Skipped"]);
        summary.CancelledLine.ShouldBe(BackupRunSummary.CancelledLine);
        _viewModel.StartBackupCancelCommand.CanExecute(null).ShouldBeFalse();
        _viewModel.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task CanClose_NoRun_ReturnsTrueWithoutAsking()
    {
        var canClose = await _viewModel.CanCloseAsync();

        canClose.ShouldBeTrue();
        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
    }

    [Fact]
    public async Task CanClose_DuringRunUserDeclines_ReturnsFalseAndKeepsRunning()
    {
        var token = UseRunThatEndsOnCancellation();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        _dialogs.Confirm(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var canClose = await _viewModel.CanCloseAsync();

        canClose.ShouldBeFalse();
        _dialogs.Received(1).Confirm(RepositoriesViewModel.CloseDuringRunTitle, RepositoriesViewModel.CloseDuringRunMessage);
        token().IsCancellationRequested.ShouldBeFalse();
        _viewModel.IsRunning.ShouldBeTrue();
        _viewModel.StartBackupCancelCommand.Execute(null);
        await execution;
    }

    [Fact]
    public async Task CanClose_DuringRunUserConfirms_CancelsAndWaitsForRun()
    {
        var token = UseRunThatEndsOnCancellation();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        _dialogs.Confirm(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var canClose = await _viewModel.CanCloseAsync();

        canClose.ShouldBeTrue();
        token().IsCancellationRequested.ShouldBeTrue();
        execution.IsCompleted.ShouldBeTrue();
        _viewModel.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task CanClose_CancellationAlreadyRequested_WaitsWithoutAsking()
    {
        UseRunThatEndsOnCancellation();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        _viewModel.StartBackupCancelCommand.Execute(null);

        var canClose = await _viewModel.CanCloseAsync();

        canClose.ShouldBeTrue();
        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
        execution.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task StartBackup_Finished_RefreshesTreeAndRestoresSelection()
    {
        var newArchivedAt = new DateTimeOffset(2026, 11, 13, 18, 45, 0, TimeSpan.FromHours(2));
        var afterRun = new RepositoryOverview(
            [new("octo", "https://github.com/octo", [new("octo", "tool", "octo", RepositoryOverviewStatus.Active, newArchivedAt, null) { RepositoryId = 1, ArchiveFolder = @"C:\backup" }])],
            []);
        _provider.LoadAsync(Arg.Any<CancellationToken>()).Returns(_overview, _overview, afterRun);
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        var before = FindNode("octo/tool");
        before.IsSelected = true;

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.Repository);

        var after = (RepositoryNodeViewModel)FindNode("octo/tool");
        after.ShouldNotBeSameAs(before);
        after.IsSelected.ShouldBeTrue();
        after.LastArchiveText.ShouldBe("2026-11-13 18:45");
        _viewModel.SelectedNode.ShouldBeSameAs(after);
        await _provider.Received(3).LoadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_CollapsedAccount_StaysCollapsed()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        FindNode("octo").IsExpanded = false;

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        FindNode("octo").IsExpanded.ShouldBeFalse();
        FindNode("empty").IsExpanded.ShouldBeTrue();
    }

    [Fact]
    public async Task StartBackup_ProgressReported_ShowsRepositoryAndOperationOnUiThread()
    {
        var (run, progress) = UseRunInProgress();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        progress().ShouldNotBeNull().Report(new(BackupProgressStage.Cloning, "octo/tool", 1, 2));

        _viewModel.HasProgress.ShouldBeFalse();
        _dispatcher.RunAll();
        _viewModel.HasProgress.ShouldBeTrue();
        _viewModel.ProgressRepository.ShouldBe("octo/tool (1 of 2)");
        _viewModel.ProgressOperation.ShouldBe("Cloning");
        run.SetResult(new([], [], []));
        await execution;
    }

    [Fact]
    public async Task StartBackup_DiscoveryReported_ShowsOperationWithoutRepository()
    {
        var (run, progress) = UseRunInProgress();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        progress().ShouldNotBeNull().Report(new(BackupProgressStage.Discovering, null, 0, 0));
        _dispatcher.RunAll();

        _viewModel.HasProgress.ShouldBeTrue();
        _viewModel.ProgressRepository.ShouldBeNull();
        _viewModel.ProgressOperation.ShouldBe("Discovering repositories on GitHub");
        run.SetResult(new([], [], []));
        await execution;
    }

    [Fact]
    public async Task StartBackup_RunEnds_ClearsProgress()
    {
        var (run, progress) = UseRunInProgress();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        progress().ShouldNotBeNull().Report(new(BackupProgressStage.Archiving, "octo/tool", 1, 1));
        _dispatcher.RunAll();

        run.SetResult(new([], [], []));
        await execution;

        _viewModel.HasProgress.ShouldBeFalse();
        _viewModel.ProgressRepository.ShouldBeNull();
        _viewModel.ProgressOperation.ShouldBeNull();
    }

    [Fact]
    public async Task StartBackup_ProgressQueuedWhenRunEnds_IsIgnored()
    {
        var (run, progress) = UseRunInProgress();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        progress().ShouldNotBeNull().Report(new(BackupProgressStage.Fetching, "octo/tool", 1, 1));

        run.SetResult(new([], [], []));
        await execution;
        _dispatcher.RunAll();

        _viewModel.HasProgress.ShouldBeFalse();
    }

    [Fact]
    public async Task StartBackup_ProgressOfPreviousRunDuringNextRun_IsIgnored()
    {
        var (first, firstProgress) = UseRunInProgress();
        var firstExecution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        var previous = firstProgress().ShouldNotBeNull();
        first.SetResult(new([], [], []));
        await firstExecution;
        var (second, secondProgress) = UseRunInProgress();
        var secondExecution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        previous.Report(new(BackupProgressStage.Archiving, "octo/old", 1, 1));
        secondProgress().ShouldNotBeNull().Report(new(BackupProgressStage.Cloning, "octo/tool", 1, 1));
        previous.Report(new(BackupProgressStage.Archiving, "octo/old", 1, 1));
        _dispatcher.RunAll();

        _viewModel.ProgressRepository.ShouldBe("octo/tool (1 of 1)");
        second.SetResult(new([], [], []));
        await secondExecution;
    }

    [Fact]
    public async Task StartBackup_RunInProgress_ShowsLogEntriesOfRun()
    {
        var (run, _) = UseRunInProgress();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        var entry = new LiveLogEntry(_archivedAt, LogLevel.Information, "Cloning octo/tool");

        _logFeed.Write(entry);
        _dispatcher.RunAll();

        _logFeed.SubscriberCount.ShouldBe(1);
        _viewModel.RunLog.Entries.ShouldBe([entry]);
        run.SetResult(new([], [], []));
        await execution;
    }

    [Fact]
    public async Task StartBackup_RunEnds_UnsubscribesAndKeepsEntries()
    {
        var (run, _) = UseRunInProgress();
        var execution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        _logFeed.Write(new(_archivedAt, LogLevel.Information, "Archiving octo/tool"));
        _dispatcher.RunAll();

        run.SetResult(new([], [], []));
        await execution;
        _logFeed.Write(new(_archivedAt, LogLevel.Information, "After the run"));
        _dispatcher.RunAll();

        _logFeed.SubscriberCount.ShouldBe(0);
        _viewModel.RunLog.Entries.Select(entry => entry.Message).ShouldBe(["Archiving octo/tool"]);
    }

    [Fact]
    public async Task StartBackup_RunFails_Unsubscribes()
    {
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Unexpected"));

        await _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _logFeed.SubscriberCount.ShouldBe(0);
    }

    [Fact]
    public async Task StartBackup_NewRun_ClearsLogOfPreviousRun()
    {
        var (first, _) = UseRunInProgress();
        var firstExecution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);
        _logFeed.Write(new(_archivedAt, LogLevel.Information, "First run"));
        _dispatcher.RunAll();
        first.SetResult(new([], [], []));
        await firstExecution;

        var (second, _) = UseRunInProgress();
        var secondExecution = _viewModel.StartBackupCommand.ExecuteAsync(BackupScope.All);

        _viewModel.RunLog.Entries.ShouldBeEmpty();
        second.SetResult(new([], [], []));
        await secondExecution;
    }

    [Theory]
    [InlineData(BackupProgressStage.CheckingRemote, "Checking the server for changes")]
    [InlineData(BackupProgressStage.Fetching, "Fetching changes")]
    [InlineData(BackupProgressStage.Preparing, "Preparing")]
    public void DescribeStage_Stage_ReturnsText(BackupProgressStage stage, string expected) =>
        RepositoriesViewModel.DescribeStage(stage).ShouldBe(expected);

    /// <summary>
    /// Makes the run service return a run that stays in progress until the test completes it.
    /// </summary>
    /// <returns>
    /// The source that completes the run and a function that returns the receiver of progress the run received.
    /// </returns>
    private (TaskCompletionSource<BackupRunResult> Run, Func<IProgress<BackupProgress>?> Progress) UseRunInProgress()
    {
        var run = new TaskCompletionSource<BackupRunResult>();
        IProgress<BackupProgress>? received = null;
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                received = call.ArgAt<IProgress<BackupProgress>?>(2);
                return run.Task;
            });
        return (run, () => received);
    }

    /// <summary>
    /// Makes the run service return a run that stays in progress until its token is cancelled and then returns the
    /// result of a cancelled run with one cancelled and one skipped item.
    /// </summary>
    /// <returns>A function that returns the token the run received.</returns>
    private Func<CancellationToken> UseRunThatEndsOnCancellation()
    {
        var received = CancellationToken.None;
        _runService.RunAsync(Arg.Any<BackupRunFilter>(), Arg.Any<bool>(), Arg.Any<IProgress<BackupProgress>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            received = call.ArgAt<CancellationToken>(3);
            var run = new TaskCompletionSource<BackupRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            received.Register(() => run.TrySetResult(new BackupRunResult(
                [
                    new("octo/tool", BackupItemStatus.Cancelled, null, TimeSpan.Zero, null),
                    new("other/lib", BackupItemStatus.Skipped, null, TimeSpan.Zero, "The run was cancelled."),
                ],
                [],
                [])
            { IsCancelled = true }));
            return run.Task;
        });
        return () => received;
    }

    /// <summary>
    /// Finds a node of the tree: <c>group</c> for the group of standalone repositories, a login for an account, or
    /// <c>owner/name</c> for a repository.
    /// </summary>
    /// <param name="path">The node to find.</param>
    /// <returns>The node.</returns>
    private TreeNodeViewModel FindNode(string path)
    {
        var key = path == "group" ? RepositoryGroupNodeViewModel.GroupKey
            : path.Split('/') is [var owner, var name] ? RepositoryNodeViewModel.CreateKey(owner, name)
            : AccountNodeViewModel.CreateKey(path);
        return _viewModel.Nodes.Concat(_viewModel.Nodes.SelectMany(node => node.Children)).Single(node => node.Key == key);
    }
}
