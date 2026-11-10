using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Tests.BackupConfiguration;

/// <summary>
/// Tests <see cref="ContainedFolder"/>.
/// </summary>
public sealed class ContainedFolderTests
{
    [Theory]
    [InlineData(@"C:\Sources", @"C:\Sources\askrinnik\my-repo")]
    [InlineData(@"C:\Sources\", @"C:\Sources\askrinnik\my-repo")]
    [InlineData(@"D:\", @"D:\askrinnik\my-repo")]
    public void Combine_NamesInsideRoot_ReturnsCombinedFolder(string root, string expected) =>
        ContainedFolder.Combine(root, "askrinnik", "my-repo").ShouldBe(expected);

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(@"..\Other")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"\\server\share")]
    public void Combine_NameEscapingRoot_Throws(string name) =>
        Should.Throw<ArgumentException>(() => ContainedFolder.Combine(@"C:\Sources", name));

    [Fact]
    public void Combine_SiblingWithRootAsPrefix_Throws() =>
        Should.Throw<ArgumentException>(() => ContainedFolder.Combine(@"C:\Sources", @"..\SourcesOther"));
}
