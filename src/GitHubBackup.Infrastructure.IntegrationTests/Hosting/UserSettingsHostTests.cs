using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.IntegrationTests.Hosting;

/// <summary>
/// Tests that the host of <see cref="GitHubBackupHostBuilder"/> reads <see cref="GitHubBackupHostBuilder.UserSettingsFileName"/>
/// after <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/> and before the command line, and that the file
/// written by <see cref="IToolPathsStore"/> takes effect in the next host.
/// </summary>
public sealed class UserSettingsHostTests
{
    /// <summary>The settings file with both tool paths set.</summary>
    private const string _appSettings = """
        { "Tools": { "GitPath": "C:\\AppSettings\\git.exe", "SevenZipPath": "C:\\AppSettings\\7z.exe" } }
        """;

    [Fact]
    public void BuildHost_UserSettingsFile_OverridesAppSettings()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);
        WriteUserSettings(contentRoot, """{ "Tools": { "GitPath": "D:\\User\\git.exe" } }""");

        using var host = contentRoot.BuildHost();

        var options = host.Services.GetRequiredService<IOptions<ToolsOptions>>().Value;
        options.GitPath.ShouldBe(@"D:\User\git.exe");
        options.SevenZipPath.ShouldBe(@"C:\AppSettings\7z.exe");
    }

    [Fact]
    public void BuildHost_UserSettingsWithOtherKeys_ReadsOnlyToolPaths()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);
        WriteUserSettings(contentRoot, """
            {
              "GitHub": { "ApiBaseUrl": "https://example.com/api", "Token": "from-user-settings" },
              "Tools": { "SevenZipPath": "D:\\User\\7z.exe" }
            }
            """);

        using var host = contentRoot.BuildHost();

        var gitHub = host.Services.GetRequiredService<IOptions<GitHubOptions>>().Value;
        gitHub.ApiBaseUrl.ShouldBe("https://api.github.com");
        host.Services.GetRequiredService<IConfiguration>()["GitHub:Token"].ShouldBeNull();
        host.Services.GetRequiredService<IOptions<ToolsOptions>>().Value.SevenZipPath.ShouldBe(@"D:\User\7z.exe");
    }

    [Fact]
    public void BuildHost_NoUserSettingsFile_KeepsAppSettings()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);

        using var host = contentRoot.BuildHost();

        host.Services.GetRequiredService<IOptions<ToolsOptions>>().Value.GitPath.ShouldBe(@"C:\AppSettings\git.exe");
    }

    [Fact]
    public void BuildHost_CommandLine_OverridesUserSettings()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);
        WriteUserSettings(contentRoot, """{ "Tools": { "GitPath": "D:\\User\\git.exe" } }""");

        using var host = contentRoot.BuildHost(@"--Tools:GitPath=E:\Arg\git.exe");

        host.Services.GetRequiredService<IOptions<ToolsOptions>>().Value.GitPath.ShouldBe(@"E:\Arg\git.exe");
    }

    [Fact]
    public void BuildHost_UserSettingsNotJson_ThrowsConfigurationFileException()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);
        WriteUserSettings(contentRoot, "{ not json");

        var exception = Should.Throw<ConfigurationFileException>(() => contentRoot.BuildHost());

        exception.Message.ShouldContain(GitHubBackupHostBuilder.UserSettingsFileName);
    }

    [Fact]
    public async Task SaveAsync_ThenNewHost_ReadsSavedPaths()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);
        using (var host = contentRoot.BuildHost())
        {
            await host.Services.GetRequiredService<IToolPathsStore>()
                .SaveAsync(new(@"D:\Saved\git.exe", null), TestContext.Current.CancellationToken);
        }

        using var nextHost = contentRoot.BuildHost();

        var paths = nextHost.Services.GetRequiredService<IToolPathsStore>().Current;
        paths.GitPath.ShouldBe(@"D:\Saved\git.exe");
        paths.SevenZipPath.ShouldBeOneOf(null, string.Empty, ToolsOptions.DefaultSevenZipPath);
    }

    /// <summary>
    /// Writes <paramref name="json"/> as the user settings file of <paramref name="contentRoot"/>.
    /// </summary>
    /// <param name="contentRoot">The content root of the host.</param>
    /// <param name="json">The JSON text of the file.</param>
    private static void WriteUserSettings(TemporaryContentRoot contentRoot, string json) =>
        File.WriteAllText(Path.Combine(contentRoot.FolderPath, GitHubBackupHostBuilder.UserSettingsFileName), json);
}
