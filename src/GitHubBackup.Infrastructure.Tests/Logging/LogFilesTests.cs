using GitHubBackup.Infrastructure.Logging;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="LogFiles"/>.
/// </summary>
public sealed class LogFilesTests
{
    [Theory]
    [InlineData(LogFiles.TextExtension, @"C:\app\logs\githubbackup-.log")]
    [InlineData(LogFiles.JsonExtension, @"C:\app\logs\githubbackup-.json")]
    public void GetPathTemplate_ContentRoot_ReturnsPathInLogsFolder(string extension, string expected) =>
        LogFiles.GetPathTemplate(@"C:\app", extension).ShouldBe(expected);

    [Theory]
    [InlineData(LogFiles.TextExtension, "githubbackup-20260107.log")]
    [InlineData(LogFiles.JsonExtension, "githubbackup-20260107.json")]
    public void GetFileName_Date_ReturnsDatedName(string extension, string expected) =>
        LogFiles.GetFileName(new(2026, 1, 7), extension).ShouldBe(expected);
}
