using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Tests.History;

/// <summary>
/// Tests <see cref="RunHistoryEntry"/>.
/// </summary>
public sealed class RunHistoryEntryTests
{
    [Fact]
    public void Count_ItemsWithStatuses_ReturnsNumberPerStatus()
    {
        var startedAt = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(3));
        var run = new RunHistoryEntry(Guid.NewGuid(), startedAt, startedAt.AddMinutes(1), RunMode.Silent, false, ExitCode.RepositoriesFailed)
        {
            Items =
            [
                new("octo/one", BackupTarget.Repository, BackupItemStatus.Archived, TimeSpan.FromSeconds(1)),
                new("octo/one", BackupTarget.Wiki, BackupItemStatus.Archived, TimeSpan.FromSeconds(1)),
                new("octo/two", BackupTarget.Repository, BackupItemStatus.Failed, TimeSpan.FromSeconds(1)),
            ],
        };

        run.Count(BackupItemStatus.Archived).ShouldBe(2);
        run.Count(BackupItemStatus.Failed).ShouldBe(1);
        run.Count(BackupItemStatus.UpToDate).ShouldBe(0);
    }
}
