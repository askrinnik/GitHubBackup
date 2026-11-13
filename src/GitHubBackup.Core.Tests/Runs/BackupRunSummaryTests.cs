using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="BackupRunSummary"/>.
/// </summary>
public sealed class BackupRunSummaryTests
{
    [Theory]
    [InlineData(false, false, new[] { BackupItemStatus.Archived, BackupItemStatus.UpToDate, BackupItemStatus.Warning, BackupItemStatus.Failed, BackupItemStatus.Skipped })]
    [InlineData(true, false, new[] { BackupItemStatus.WouldClone, BackupItemStatus.WouldArchive, BackupItemStatus.UpToDate, BackupItemStatus.Warning, BackupItemStatus.Failed, BackupItemStatus.Skipped })]
    [InlineData(false, true, new[] { BackupItemStatus.Archived, BackupItemStatus.UpToDate, BackupItemStatus.Warning, BackupItemStatus.Failed, BackupItemStatus.Skipped, BackupItemStatus.Cancelled })]
    [InlineData(true, true, new[] { BackupItemStatus.WouldClone, BackupItemStatus.WouldArchive, BackupItemStatus.UpToDate, BackupItemStatus.Warning, BackupItemStatus.Failed, BackupItemStatus.Skipped, BackupItemStatus.Cancelled })]
    public void GetTotalStatuses_KindOfRun_ListsStatusesInOrder(bool dryRun, bool cancelled, BackupItemStatus[] expected)
    {
        var result = new BackupRunResult([], [], []) { IsDryRun = dryRun, IsCancelled = cancelled };

        BackupRunSummary.GetTotalStatuses(result).ShouldBe(expected);
    }

    [Fact]
    public void FormatTotals_CancelledRun_CountsEveryListedStatus()
    {
        var result = new BackupRunResult(
            [
                Item("octo/a", BackupItemStatus.Archived),
                Item("octo/b", BackupItemStatus.Archived),
                Item("octo/c", BackupItemStatus.Cancelled),
                Item("octo/d", BackupItemStatus.Skipped),
            ],
            [],
            [])
        { IsCancelled = true };

        BackupRunSummary.FormatTotals(result)
            .ShouldBe("Archived: 2, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 1, Cancelled: 1");
    }

    [Theory]
    [InlineData(0, false, false, true)]
    [InlineData(1, false, false, false)]
    [InlineData(0, true, false, false)]
    [InlineData(0, false, true, false)]
    public void ShowsNoRepositoriesLine_Result_OnlyForRunWithoutItems(
        int itemCount, bool cancelled, bool stopped, bool expected)
    {
        var result = new BackupRunResult(
            [.. Enumerable.Range(0, itemCount).Select(index => Item($"octo/r{index}", BackupItemStatus.UpToDate))],
            [],
            stopped ? ["Git was not found."] : [])
        { IsCancelled = cancelled };

        BackupRunSummary.ShowsNoRepositoriesLine(result).ShouldBe(expected);
    }

    [Fact]
    public void DescribeDiscovery_EveryKind_ListsNewMissingRenamedReturnedInOrder()
    {
        var discovery = new DiscoveryResult(
            [new("octocat", Repository(7, "octocat", "fresh"))],
            [Repository(8, "other", "shared")],
            [new("octocat", "gone", 3, RepositoryStatus.Active, "octocat")],
            [new(new("octocat", "back", 4, RepositoryStatus.Unavailable, "octocat"), Repository(4, "octocat", "back"))],
            [new(new("octocat", "old", 5, RepositoryStatus.Active, "octocat"), Repository(5, "octocat", "new"))]);

        BackupRunSummary.DescribeDiscovery(discovery).ShouldBe(
        [
            "New: octocat/fresh",
            "New: other/shared",
            "Missing: octocat/gone",
            "Renamed: octocat/old -> octocat/new",
            "Returned: octocat/back",
        ]);
    }

    [Theory]
    [InlineData(false, BackupRunSummary.DryRunHeader)]
    [InlineData(true, BackupRunSummary.DryRunSavedIdsHeader)]
    public void GetDryRunHeader_SavedIds_NamesOnlyWhatWasSaved(bool savedIds, string expected)
    {
        var result = new BackupRunResult([], [], []) { IsDryRun = true, SavedRepositoryIds = savedIds ? ["octo/tool"] : [] };

        BackupRunSummary.GetDryRunHeader(result).ShouldBe(expected);
    }

    [Fact]
    public void DryRunSavedIdsHeader_Text_SaysOnlyIdsWereSaved() =>
        BackupRunSummary.DryRunSavedIdsHeader.ShouldBe(
            "Dry run: nothing was cloned, updated or archived; only the missing repository ids were saved to backup-config.json.");

    [Fact]
    public void DescribeDiscovery_Empty_ReturnsNoLine() =>
        BackupRunSummary.DescribeDiscovery(DiscoveryResult.Empty).ShouldBeEmpty();

    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(420, "0:00.4")]
    [InlineData(75_340, "1:15.3")]
    [InlineData(3_725_999, "62:05.9")]
    public void FormatDuration_Milliseconds_FormatsMinutesSecondsTenths(int milliseconds, string expected) =>
        BackupRunSummary.FormatDuration(TimeSpan.FromMilliseconds(milliseconds)).ShouldBe(expected);

    /// <summary>
    /// Creates the result of a repository with <paramref name="status"/>.
    /// </summary>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    /// <param name="status">The outcome.</param>
    /// <returns>The result without archive or message.</returns>
    private static BackupItemResult Item(string repository, BackupItemStatus status) =>
        new(repository, status, null, TimeSpan.Zero, null);

    /// <summary>
    /// Creates a repository as GitHub reports it.
    /// </summary>
    /// <param name="id">The GitHub id.</param>
    /// <param name="owner">The owner.</param>
    /// <param name="name">The name.</param>
    /// <returns>The repository.</returns>
    private static GitHubRepository Repository(long id, string owner, string name) =>
        new(id, owner, name, false, false, false, false, "main", $"https://github.com/{owner}/{name}");
}
