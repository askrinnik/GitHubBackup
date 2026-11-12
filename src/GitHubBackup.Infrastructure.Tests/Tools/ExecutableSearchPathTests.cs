using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Infrastructure.Tools;

namespace GitHubBackup.Infrastructure.Tests.Tools;

/// <summary>
/// Tests <see cref="ExecutableSearchPath"/> with a fake file system.
/// </summary>
public sealed class ExecutableSearchPathTests
{
    private const string _fileName = "tool.exe";
    private const string _toolPath = @"C:\Program Files\Tool\tool.exe";

    private readonly MockFileSystem _fileSystem = new();

    /// <summary>
    /// Creates the fixture with the tool installed in one folder and the current directory set to its parent.
    /// </summary>
    public ExecutableSearchPathTests()
    {
        _fileSystem.AddFile(_toolPath, new MockFileData(string.Empty));
        _fileSystem.AddFile(@"D:\Other\tool.exe", new MockFileData(string.Empty));
        _fileSystem.Directory.SetCurrentDirectory(@"C:\Program Files");
    }

    [Theory]
    [InlineData(@"C:\Program Files\Tool")]
    [InlineData(@"C:\Windows;C:\Program Files\Tool;D:\Other")]
    [InlineData(@"  ""C:\Program Files\Tool""  ")]
    [InlineData(@";;C:\Program Files\Tool\;")]
    public void Find_FolderInPath_ReturnsFirstMatch(string pathVariable)
    {
        var path = ExecutableSearchPath.Find(_fileSystem, pathVariable, _fileName);

        path.ShouldBe(_toolPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"Tool;.\Tool;\Program Files\Tool;C:Tool")]
    public void Find_NoFullyQualifiedFolderHasTool_ReturnsNull(string? pathVariable)
    {
        var path = ExecutableSearchPath.Find(_fileSystem, pathVariable, _fileName);

        path.ShouldBeNull();
    }
}
