using GitHubBackup.Core.Configuration;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="ContentRootPathPostConfigure"/>.
/// </summary>
public sealed class ContentRootPathPostConfigureTests
{
    private readonly ContentRootPathPostConfigure _postConfigure;

    /// <summary>
    /// Creates the subject over a host environment whose content root is <c>C:\app\</c>.
    /// </summary>
    public ContentRootPathPostConfigureTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(@"C:\app\");
        _postConfigure = new(environment);
    }

    [Theory]
    [InlineData("backup-config.json", @"C:\app\backup-config.json")]
    [InlineData(@"config\backup-config.json", @"C:\app\config\backup-config.json")]
    [InlineData(@"..\shared\backup-config.json", @"C:\shared\backup-config.json")]
    [InlineData(@"D:\backup\backup-config.json", @"D:\backup\backup-config.json")]
    public void PostConfigure_BackupConfigPath_ResolvesAgainstContentRoot(string configured, string expected)
    {
        var options = new BackupOptions { ConfigPath = configured };

        _postConfigure.PostConfigure(null, options);

        options.ConfigPath.ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"data\history.db", @"C:\app\data\history.db")]
    [InlineData(@"E:\history\history.db", @"E:\history\history.db")]
    public void PostConfigure_HistoryDatabasePath_ResolvesAgainstContentRoot(string configured, string expected)
    {
        var options = new HistoryOptions { DatabasePath = configured };

        _postConfigure.PostConfigure(null, options);

        options.DatabasePath.ShouldBe(expected);
    }

    [Fact]
    public void PostConfigure_EmptyPath_LeavesPathForValidator()
    {
        var options = new HistoryOptions { DatabasePath = "" };

        _postConfigure.PostConfigure(null, options);

        options.DatabasePath.ShouldBe("");
    }
}
