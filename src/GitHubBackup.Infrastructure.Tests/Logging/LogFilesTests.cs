using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Time.Testing;

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

    [Fact]
    public void GetTextLogPath_LocalDate_ReturnsDatedPathInLogsFolder()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 7, 23, 30, 0, TimeSpan.Zero));
        timeProvider.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2"));

        LogFiles.GetTextLogPath(@"C:\app", timeProvider).ShouldBe(@"C:\app\logs\githubbackup-20260108.log");
    }
}
