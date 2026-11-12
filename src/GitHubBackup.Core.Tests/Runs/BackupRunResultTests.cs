using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="BackupRunResult"/>.
/// </summary>
public sealed class BackupRunResultTests
{
    [Theory]
    [InlineData(new BackupItemStatus[0], ExitCode.Success)]
    [InlineData(new[] { BackupItemStatus.Archived, BackupItemStatus.UpToDate, BackupItemStatus.Warning, BackupItemStatus.Skipped }, ExitCode.Success)]
    [InlineData(new[] { BackupItemStatus.Archived, BackupItemStatus.Failed }, ExitCode.RepositoriesFailed)]
    public void ExitCode_NotStopped_DependsOnFailedItems(BackupItemStatus[] statuses, ExitCode expected)
    {
        var result = new BackupRunResult([.. statuses.Select(Item)], [], []);

        result.ExitCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData("added", ExitCode.DiscoveryEvents)]
    [InlineData("skipped", ExitCode.DiscoveryEvents)]
    [InlineData("excluded", ExitCode.DiscoveryEvents)]
    [InlineData("unavailable", ExitCode.DiscoveryEvents)]
    [InlineData("renamed", ExitCode.DiscoveryEvents)]
    [InlineData("reactivated", ExitCode.Success)]
    [InlineData("none", ExitCode.Success)]
    public void ExitCode_DiscoveryWithoutFailures_IsDiscoveryEventsForNewMissingOrRenamed(string kind, ExitCode expected)
    {
        var result = new BackupRunResult([Item(BackupItemStatus.Archived)], [], []) { Discovery = Discovery(kind) };

        result.ExitCode.ShouldBe(expected);
    }

    public static TheoryData<bool, bool, bool, ExitCode> ConditionCombinations => new()
    {
        { false, false, false, ExitCode.Success },
        { false, false, true, ExitCode.DiscoveryEvents },
        { false, true, false, ExitCode.RepositoriesFailed },
        { false, true, true, ExitCode.RepositoriesFailed },
        { true, false, false, ExitCode.Critical },
        { true, false, true, ExitCode.Critical },
        { true, true, false, ExitCode.Critical },
        { true, true, true, ExitCode.Critical },
    };

    [Theory]
    [MemberData(nameof(ConditionCombinations))]
    public void ExitCode_AllConditionCombinations_FirstMatchingFromTopWins(
        bool critical, bool failed, bool discoveryEvents, ExitCode expected)
    {
        var result = new BackupRunResult(
            failed ? [Item(BackupItemStatus.Failed)] : [Item(BackupItemStatus.Archived)],
            [],
            critical ? ["error"] : [])
        {
            Discovery = Discovery(discoveryEvents ? "added" : "none"),
        };

        result.ExitCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData("newInAccount", ExitCode.DiscoveryEvents)]
    [InlineData("newStandalone", ExitCode.DiscoveryEvents)]
    [InlineData("missing", ExitCode.DiscoveryEvents)]
    [InlineData("renamed", ExitCode.DiscoveryEvents)]
    [InlineData("returned", ExitCode.Success)]
    public void ExitCode_PlannedDiscoveryWithoutFailures_IsDiscoveryEventsForNewMissingOrRenamed(string kind, ExitCode expected)
    {
        var result = new BackupRunResult([Item(BackupItemStatus.WouldArchive)], [], [])
        {
            IsDryRun = true,
            PlannedDiscovery = Planned(kind),
        };

        result.ExitCode.ShouldBe(expected);
    }

    [Fact]
    public void ExitCode_PlannedDiscoveryAndFailedItem_IsRepositoriesFailed()
    {
        var result = new BackupRunResult([Item(BackupItemStatus.Failed)], [], [])
        {
            IsDryRun = true,
            PlannedDiscovery = Planned("missing"),
        };

        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
    }

    [Fact]
    public void Stopped_ErrorsAndWarnings_HasNoItemsAndIsCritical()
    {
        var result = BackupRunResult.Stopped(["error"], ["warning"]);

        result.Items.ShouldBeEmpty();
        result.CriticalErrors.ShouldBe(["error"]);
        result.ConfigWarnings.ShouldBe(["warning"]);
        result.ExitCode.ShouldBe(ExitCode.Critical);
    }

    [Fact]
    public void Count_MixedStatuses_CountsEachStatus()
    {
        var result = new BackupRunResult(
            [Item(BackupItemStatus.Archived), Item(BackupItemStatus.Archived), Item(BackupItemStatus.Failed)], [], []);

        result.Count(BackupItemStatus.Archived).ShouldBe(2);
        result.Count(BackupItemStatus.Failed).ShouldBe(1);
        result.Count(BackupItemStatus.Skipped).ShouldBe(0);
    }

    /// <summary>
    /// Creates the result of one repository with <paramref name="status"/>.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The item.</returns>
    private static BackupItemResult Item(BackupItemStatus status) => new("owner/repo", status, null, TimeSpan.Zero, null);

    /// <summary>
    /// Creates what discovery found in a dry run, with one repository in the class named by <paramref name="kind"/>.
    /// </summary>
    /// <param name="kind">
    /// <c>newInAccount</c>, <c>newStandalone</c>, <c>missing</c>, <c>renamed</c> or <c>returned</c>.
    /// </param>
    /// <returns>The discovery result.</returns>
    private static DiscoveryResult Planned(string kind)
    {
        var repository = new GitHubRepository(1, "owner", "repo", false, false, false, false, "main", "https://github.com/owner/repo");
        var tracked = new TrackedRepository("owner", "old", 1, RepositoryStatus.Active, "owner");
        return new(
            kind == "newInAccount" ? [new("owner", repository)] : [],
            kind == "newStandalone" ? [repository] : [],
            kind == "missing" ? [tracked] : [],
            kind == "returned" ? [new(tracked with { Status = RepositoryStatus.Unavailable }, repository)] : [],
            kind == "renamed" ? [new(tracked, repository)] : []);
    }

    /// <summary>
    /// Creates what discovery changed, with one repository in the list named by <paramref name="kind"/>.
    /// </summary>
    /// <param name="kind">
    /// <c>added</c>, <c>skipped</c>, <c>excluded</c>, <c>unavailable</c>, <c>renamed</c>, <c>reactivated</c>, or
    /// <c>none</c> for no repository at all.
    /// </param>
    /// <returns>The apply result.</returns>
    private static DiscoveryApplyResult Discovery(string kind)
    {
        string[] one = ["owner/repo"];
        RenameOutcome[] renamed = kind == "renamed"
            ? [new("owner/old", "owner/repo", RenameDecision.KeepArchiveFolder, RenameDecision.KeepArchiveFolder, CloneRelocationResult.Moved, CloneRelocationResult.NotPresent)]
            : [];
        return new(
            kind == "added" ? one : [],
            kind == "skipped" ? one : [],
            kind == "excluded" ? one : [],
            kind == "unavailable" ? one : [],
            kind == "reactivated" ? one : [],
            renamed,
            ConfigSaved: kind != "none");
    }
}
