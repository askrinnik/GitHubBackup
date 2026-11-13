using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="RunSummaryViewModel"/>: the content of the summary for each kind of run.
/// </summary>
public sealed class RunSummaryViewModelTests
{
    private const string _logPath = @"C:\app\logs\githubbackup-20261113.log";

    private const string _token = "ghp_AppTestTokenValue0123456789abcdefABC";

    private readonly SecretMasker _masker = new();

    [Fact]
    public void FromResult_Items_BuildsRowsTotalsAndLogPath()
    {
        var result = new BackupRunResult(
            [
                new("octo/tool", BackupItemStatus.Archived, "octo_tool_20261113-120000_0123456789.zip", TimeSpan.FromSeconds(75.34), null),
                new("octo/broken", BackupItemStatus.Failed, null, TimeSpan.FromSeconds(3), "git exited with code 128."),
            ],
            [],
            []);

        var summary = RunSummaryViewModel.FromResult(result, _logPath, _masker);

        summary.Rows.ShouldBe(
        [
            new("octo/tool", "Archived", "octo_tool_20261113-120000_0123456789.zip", "1:15.3", string.Empty),
            new("octo/broken", "Failed", string.Empty, "0:03.0", "git exited with code 128."),
        ]);
        summary.HasItems.ShouldBeTrue();
        summary.TotalsLine.ShouldBe("Archived: 1, UpToDate: 0, Warning: 0, Failed: 1, Skipped: 0");
        summary.LogPath.ShouldBe(_logPath);
        summary.Header.ShouldBeNull();
        summary.Errors.ShouldBeEmpty();
        summary.HasErrors.ShouldBeFalse();
        summary.Warnings.ShouldBeEmpty();
        summary.NoRepositoriesLine.ShouldBeNull();
        summary.CancelledLine.ShouldBeNull();
        summary.DiscoveryLines.ShouldBeEmpty();
    }

    [Fact]
    public void FromResult_CriticalErrors_ShowsErrorsWarningsAndLogPathWithoutTable()
    {
        var result = BackupRunResult.Stopped(
            [BackupOrchestrator.RunLockHeldMessage, "Git was not found."], ["Account 'octo' is listed twice."]);

        var summary = RunSummaryViewModel.FromResult(result, _logPath, _masker);

        summary.Errors.ShouldBe([BackupOrchestrator.RunLockHeldMessage, "Git was not found."]);
        summary.HasErrors.ShouldBeTrue();
        summary.Warnings.ShouldBe(["Account 'octo' is listed twice."]);
        summary.Rows.ShouldBeEmpty();
        summary.HasItems.ShouldBeFalse();
        summary.TotalsLine.ShouldBeNull();
        summary.NoRepositoriesLine.ShouldBeNull();
        summary.LogPath.ShouldBe(_logPath);
    }

    [Fact]
    public void FromResult_ConfigAndHistoryWarnings_ListsConfigWarningsThenHistoryWarning()
    {
        var result = new BackupRunResult([], ["Account 'octo' is listed twice."], [])
        {
            HistoryWarning = "The run was not recorded in the run history.",
        };

        var summary = RunSummaryViewModel.FromResult(result, _logPath, _masker);

        summary.Warnings.ShouldBe(["Account 'octo' is listed twice.", "The run was not recorded in the run history."]);
        summary.HasWarnings.ShouldBeTrue();
    }

    [Fact]
    public void FromResult_DryRun_ShowsHeaderDiscoveryAndDryRunTotals()
    {
        var result = new BackupRunResult(
            [new("octo/tool", BackupItemStatus.WouldArchive, "octo_tool.zip", TimeSpan.FromSeconds(2), null)], [], [])
        {
            IsDryRun = true,
            PlannedDiscovery = new(
                [new("octo", new GitHubRepository(7, "octo", "fresh", false, false, false, false, "main", "https://github.com/octo/fresh"))],
                [],
                [new("octo", "gone", 3, RepositoryStatus.Active, "octo")],
                [],
                []),
        };

        var summary = RunSummaryViewModel.FromResult(result, _logPath, _masker);

        summary.Header.ShouldBe(BackupRunSummary.DryRunHeader);
        summary.DiscoveryLines.ShouldBe(["New: octo/fresh", "Missing: octo/gone"]);
        summary.HasDiscoveryLines.ShouldBeTrue();
        summary.TotalsLine.ShouldBe("WouldClone: 0, WouldArchive: 1, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 0");
    }

    [Fact]
    public void FromResult_DryRunSavedIds_ShowsSavedIdsHeader()
    {
        var result = new BackupRunResult([], [], []) { IsDryRun = true, SavedRepositoryIds = ["octo/tool"] };

        var summary = RunSummaryViewModel.FromResult(result, _logPath, _masker);

        summary.Header.ShouldBe(BackupRunSummary.DryRunSavedIdsHeader);
    }

    [Fact]
    public void FromResult_Cancelled_ShowsCancelledAndSkippedRowsCancelLineAndCancelledTotal()
    {
        var result = new BackupRunResult(
            [
                new("octo/a", BackupItemStatus.UpToDate, null, TimeSpan.FromSeconds(1), null),
                new("octo/b", BackupItemStatus.Cancelled, null, TimeSpan.FromSeconds(2), null),
                new("octo/c", BackupItemStatus.Skipped, null, TimeSpan.Zero, "The run was cancelled."),
            ],
            [],
            [])
        { IsCancelled = true };

        var summary = RunSummaryViewModel.FromResult(result, _logPath, _masker);

        summary.Rows.Select(row => row.Status).ShouldBe(["UpToDate", "Cancelled", "Skipped"]);
        summary.CancelledLine.ShouldBe(BackupRunSummary.CancelledLine);
        summary.TotalsLine.ShouldBe("Archived: 0, UpToDate: 1, Warning: 0, Failed: 0, Skipped: 1, Cancelled: 1");
        summary.NoRepositoriesLine.ShouldBeNull();
    }

    [Fact]
    public void FromResult_NoItems_ShowsNoRepositoriesLine()
    {
        var summary = RunSummaryViewModel.FromResult(new([], [], []), _logPath, _masker);

        summary.NoRepositoriesLine.ShouldBe(BackupRunSummary.NoRepositoriesLine);
        summary.HasItems.ShouldBeFalse();
        summary.TotalsLine.ShouldBe("Archived: 0, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 0");
    }

    [Fact]
    public void FromResult_TextsWithToken_MasksToken()
    {
        var item = new BackupItemResult("octo/tool", BackupItemStatus.Failed, null, TimeSpan.Zero, $"Fetch failed: {_token}");
        var stopped = BackupRunResult.Stopped([$"Token {_token} is invalid."], [$"Value {_token} ignored."]);
        var failed = new BackupRunResult([item], [], []) { HistoryWarning = $"History failed: {_token}" };

        var stoppedSummary = RunSummaryViewModel.FromResult(stopped, _logPath, _masker);
        var failedSummary = RunSummaryViewModel.FromResult(failed, _logPath, _masker);

        string[] texts =
        [
            .. stoppedSummary.Errors,
            .. stoppedSummary.Warnings,
            .. failedSummary.Warnings,
            .. failedSummary.Rows.Select(row => row.Error),
        ];
        texts.ShouldAllBe(text => !text.Contains(_token) && text.Contains(SecretMasker.Replacement));
    }

    [Fact]
    public void CancelledBeforeStart_ShowsCancelLineAndLogPathOnly()
    {
        var summary = RunSummaryViewModel.CancelledBeforeStart(_logPath);

        summary.CancelledLine.ShouldBe(BackupRunSummary.CancelledLine);
        summary.LogPath.ShouldBe(_logPath);
        summary.Rows.ShouldBeEmpty();
        summary.Errors.ShouldBeEmpty();
        summary.TotalsLine.ShouldBeNull();
        summary.NoRepositoriesLine.ShouldBeNull();
    }
}
