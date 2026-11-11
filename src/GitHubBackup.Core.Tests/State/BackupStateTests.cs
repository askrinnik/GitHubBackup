using GitHubBackup.Core.Git;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Tests.State;

/// <summary>
/// Tests <see cref="BackupState"/>.
/// </summary>
public sealed class BackupStateTests
{
    [Fact]
    public void CreateEmpty_Always_HasSchemaVersion1AndEmptySections()
    {
        var state = BackupState.CreateEmpty(123456789, "askrinnik/my-repo");

        state.SchemaVersion.ShouldBe(1);
        state.RepositoryId.ShouldBe(123456789);
        state.FullName.ShouldBe("askrinnik/my-repo");
        state.Repository.Archives.ShouldBeEmpty();
        state.Wiki.Archives.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BackupTarget.Repository, BackupTarget.Wiki)]
    [InlineData(BackupTarget.Wiki, BackupTarget.Repository)]
    public void WithArchive_Target_AppendsOnlyToThatSection(BackupTarget target, BackupTarget other)
    {
        var record = CreateRecord("first.zip");

        var state = BackupState.CreateEmpty(1, "owner/repo").WithArchive(target, record, "owner/repo");

        state.GetSection(target).Archives.ShouldBe([record]);
        state.GetSection(other).Archives.ShouldBeEmpty();
    }

    [Fact]
    public void WithArchive_Twice_LastArchiveIsSecond()
    {
        var first = CreateRecord("first.zip");
        var second = CreateRecord("second.zip");

        var state = BackupState.CreateEmpty(1, "owner/repo")
            .WithArchive(BackupTarget.Repository, first, "owner/repo")
            .WithArchive(BackupTarget.Repository, second, "owner/repo");

        state.Repository.Archives.ShouldBe([first, second]);
        state.GetLastArchive(BackupTarget.Repository).ShouldBe(second);
    }

    [Fact]
    public void WithArchive_NewFullName_UpdatesFullName()
    {
        var state = BackupState.CreateEmpty(1, "owner/old-name")
            .WithArchive(BackupTarget.Repository, CreateRecord("a.zip"), "owner/new-name");

        state.FullName.ShouldBe("owner/new-name");
        state.RepositoryId.ShouldBe(1);
    }

    [Fact]
    public void WithArchive_Always_LeavesOriginalStateUnchanged()
    {
        var original = BackupState.CreateEmpty(1, "owner/repo");

        _ = original.WithArchive(BackupTarget.Repository, CreateRecord("a.zip"), "owner/renamed");

        original.Repository.Archives.ShouldBeEmpty();
        original.FullName.ShouldBe("owner/repo");
    }

    [Theory]
    [InlineData(BackupTarget.Repository)]
    [InlineData(BackupTarget.Wiki)]
    public void GetLastArchive_EmptySection_ReturnsNull(BackupTarget target) =>
        BackupState.CreateEmpty(1, "owner/repo").GetLastArchive(target).ShouldBeNull();

    [Fact]
    public void GetSection_UndefinedTarget_ThrowsArgumentOutOfRangeException() =>
        Should.Throw<ArgumentOutOfRangeException>(() => BackupState.CreateEmpty(1, "owner/repo").GetSection((BackupTarget)42));

    /// <summary>
    /// Creates a valid record of an archive named <paramref name="file"/>.
    /// </summary>
    /// <param name="file">The file name of the archive.</param>
    /// <returns>The record.</returns>
    private static ArchiveRecord CreateRecord(string file) =>
        new(file, new(2026, 10, 2, 14, 30, 15, TimeSpan.FromHours(3)), "main", "a1b2c3", 10, RefSnapshot.Empty);
}
