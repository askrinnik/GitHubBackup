using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Tests.BackupConfiguration;

/// <summary>
/// Tests <see cref="ArchiveFolderComparison"/>.
/// </summary>
public sealed class ArchiveFolderComparisonTests
{
    [Theory]
    [InlineData(@"C:\backup\octo\tool", @"C:\backup\octo\tool")]
    [InlineData(@"C:\backup\octo\tool", @"c:\BACKUP\Octo\Tool")]
    [InlineData(@"C:\backup\octo\tool\", @"C:\backup\octo\tool")]
    [InlineData(@"C:\backup\octo\tool", @"C:\backup\octo\tool/")]
    public void IsSameFolder_DifferOnlyInCaseOrTrailingSeparator_ReturnsTrue(string first, string second) =>
        ArchiveFolderComparison.IsSameFolder(first, second).ShouldBeTrue();

    [Theory]
    [InlineData(@"C:\backup\octo\tool", @"C:\backup\octo\tool2")]
    [InlineData(@"C:\backup\octo\tool", @"D:\backup\octo\tool")]
    public void IsSameFolder_DifferentFolders_ReturnsFalse(string first, string second) =>
        ArchiveFolderComparison.IsSameFolder(first, second).ShouldBeFalse();
}
