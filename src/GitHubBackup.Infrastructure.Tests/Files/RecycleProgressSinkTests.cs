using GitHubBackup.Infrastructure.Files;

namespace GitHubBackup.Infrastructure.Tests.Files;

/// <summary>
/// Tests what <see cref="RecycleProgressSink"/> records about a deletion.
/// </summary>
public sealed class RecycleProgressSinkTests
{
    [Fact]
    public void PostDeleteItem_ItemInRecycleBin_RecordsMove()
    {
        var sink = new RecycleProgressSink();

        sink.PostDeleteItem(ShellRecycleBin.DeleteRecycleIfPossible, 0, 0, 1);

        sink.DeleteResult.ShouldBe(0);
        sink.MovedToRecycleBin.ShouldBeTrue();
    }

    [Fact]
    public void PostDeleteItem_NoItemInRecycleBin_RecordsNoMove()
    {
        var sink = new RecycleProgressSink();

        sink.PostDeleteItem(ShellRecycleBin.DeleteRecycleIfPossible, 0, 0, 0);

        sink.DeleteResult.ShouldBe(0);
        sink.MovedToRecycleBin.ShouldBeFalse();
    }

    [Fact]
    public void PostDeleteItem_FailedDeletion_RecordsResult()
    {
        var sink = new RecycleProgressSink();

        sink.PostDeleteItem(ShellRecycleBin.DeleteRecycleIfPossible, 0, unchecked((int)0x80070020), 0);

        sink.DeleteResult.ShouldBe(unchecked((int)0x80070020));
    }
}
