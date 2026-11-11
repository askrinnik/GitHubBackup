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

    [Fact]
    public void ExitCode_StoppedWithFailedItems_IsCritical()
    {
        var result = new BackupRunResult([Item(BackupItemStatus.Failed)], [], ["git was not found."]);

        result.ExitCode.ShouldBe(ExitCode.Critical);
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
}
