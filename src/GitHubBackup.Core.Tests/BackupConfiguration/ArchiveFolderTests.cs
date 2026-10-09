using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Tests.BackupConfiguration;

/// <summary>
/// Tests <see cref="ArchiveFolder"/>.
/// </summary>
public sealed class ArchiveFolderTests
{
    [Fact]
    public void Resolve_NoPaths_ReturnsOwnerAndRepositoryUnderBackupRoot() =>
        ArchiveFolder.Resolve(@"C:\Backups", null, "askrinnik", "my-repo", null).ShouldBe(@"C:\Backups\askrinnik\my-repo");

    [Fact]
    public void Resolve_AccountPathSet_ReplacesOwnerFolder() =>
        ArchiveFolder.Resolve(@"C:\Backups", @"E:\Work\AMTOSS", "AMTOSS", "tool", null).ShouldBe(@"E:\Work\AMTOSS\tool");

    [Theory]
    [InlineData("askrinnik", "..")]
    [InlineData("askrinnik", @"..\..\Windows")]
    [InlineData("..", "..")]
    [InlineData("askrinnik", @"C:\Windows")]
    [InlineData(@"..\..", "repo")]
    public void Resolve_NamesEscapingBackupRoot_Throws(string owner, string repository) =>
        Should.Throw<ArgumentException>(() => ArchiveFolder.Resolve(@"C:\Backups", null, owner, repository, null));

    [Theory]
    [InlineData("..")]
    [InlineData(@"..\Other")]
    [InlineData(@"D:\Elsewhere")]
    [InlineData(".")]
    public void Resolve_NameEscapingAccountPath_Throws(string repository) =>
        Should.Throw<ArgumentException>(() => ArchiveFolder.Resolve(@"C:\Backups", @"E:\Work\AMTOSS", "AMTOSS", repository, null));

    [Fact]
    public void Resolve_DriveRootAsBackupRoot_ReturnsFolderUnderIt() =>
        ArchiveFolder.Resolve(@"D:\", null, "o", "r", null).ShouldBe(@"D:\o\r");

    [Theory]
    [InlineData(null)]
    [InlineData(@"E:\Work\AMTOSS")]
    public void Resolve_RepositoryPathSet_ReplacesWholeFolder(string? accountPath) =>
        ArchiveFolder.Resolve(@"C:\Backups", accountPath, "askrinnik", "old-tool", @"E:\Archive\old-tool")
            .ShouldBe(@"E:\Archive\old-tool");
}
