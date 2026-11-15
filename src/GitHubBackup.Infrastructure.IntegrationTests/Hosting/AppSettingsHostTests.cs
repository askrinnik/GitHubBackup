using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Core.Tools;
using GitHubBackup.Core.Ui;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.IntegrationTests.Hosting;

/// <summary>
/// Tests that the host of <see cref="GitHubBackupHostBuilder"/> runs without
/// <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/>, reads it when present, leaves
/// <see cref="GitHubBackupHostBuilder.UiSettingsFileName"/> and <c>user-settings.json</c> out of the configuration,
/// and that what the settings editor saves takes effect in the next host.
/// </summary>
public sealed class AppSettingsHostTests
{
    /// <summary>The name of a settings file that the application neither reads nor writes.</summary>
    private const string _legacyUserSettingsFileName = "user-settings.json";

    /// <summary>The settings file with both tool paths set.</summary>
    private const string _appSettings = """
        { "Tools": { "GitPath": "C:\\AppSettings\\git.exe", "SevenZipPath": "C:\\AppSettings\\7z.exe" } }
        """;

    [Fact]
    public async Task StartAsync_NoAppSettings_StartsWithDefaults()
    {
        using var contentRoot = new TemporaryContentRoot();
        using var host = contentRoot.BuildHost();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var services = host.Services;
        services.GetRequiredService<IOptions<ToolsOptions>>().Value.SevenZipPath.ShouldBe(ToolsOptions.DefaultSevenZipPath);
        services.GetRequiredService<IOptions<ToolsOptions>>().Value.GitPath.ShouldBeNull();
        services.GetRequiredService<IOptions<GitHubOptions>>().Value.ApiBaseUrl.ShouldBe("https://api.github.com");
        services.GetRequiredService<IOptions<BackupOptions>>().Value.ShortHashLength.ShouldBe(10);
        services.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value.Enabled.ShouldBeFalse();
        var serilog = services.GetRequiredService<IOptions<SerilogOptions>>().Value;
        serilog.Default.ShouldBe("Information");
        serilog.Override[SerilogOptions.HostingLifetimeSource].ShouldBe("Warning");
        await host.StopAsync(TestContext.Current.CancellationToken);
        File.Exists(Path.Combine(contentRoot.FolderPath, GitHubBackupHostBuilder.AppSettingsFileName)).ShouldBeFalse();
    }

    [Fact]
    public void BuildHost_AppSettings_OverridesDefaults()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);

        using var host = contentRoot.BuildHost();

        var options = host.Services.GetRequiredService<IOptions<ToolsOptions>>().Value;
        options.GitPath.ShouldBe(@"C:\AppSettings\git.exe");
        options.SevenZipPath.ShouldBe(@"C:\AppSettings\7z.exe");
    }

    [Fact]
    public void BuildHost_CommandLine_OverridesAppSettings()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);

        using var host = contentRoot.BuildHost(@"--Tools:GitPath=E:\Arg\git.exe");

        var options = host.Services.GetRequiredService<IOptions<ToolsOptions>>().Value;
        options.GitPath.ShouldBe(@"E:\Arg\git.exe");
        options.SevenZipPath.ShouldBe(@"C:\AppSettings\7z.exe");
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[ 1, 2 ]")]
    [InlineData("\"text\"")]
    public void BuildHost_AppSettingsNotJsonObject_ThrowsConfigurationFileExceptionNamingFile(string content)
    {
        using var contentRoot = new TemporaryContentRoot(content);

        var exception = Should.Throw<ConfigurationFileException>(() => contentRoot.BuildHost());

        exception.Message.ShouldContain(GitHubBackupHostBuilder.AppSettingsFileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \r\n")]
    public void BuildHost_EmptyAppSettings_ThrowsConfigurationFileExceptionNamingFile(string content)
    {
        using var contentRoot = new TemporaryContentRoot(content);

        var exception = Should.Throw<ConfigurationFileException>(() => contentRoot.BuildHost());

        exception.Message.ShouldContain(GitHubBackupHostBuilder.AppSettingsFileName);
    }

    [Fact]
    public void BuildHost_LegacyUserSettingsFile_IsIgnored()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);
        WriteFile(contentRoot, _legacyUserSettingsFileName, """
            {
              "Tools": { "GitPath": "D:\\User\\git.exe" },
              "OpenTelemetry": { "Enabled": true },
              "Ui": { "Theme": "Dark" }
            }
            """);

        using var host = contentRoot.BuildHost();

        host.Services.GetRequiredService<IOptions<ToolsOptions>>().Value.GitPath.ShouldBe(@"C:\AppSettings\git.exe");
        host.Services.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_LegacyUserSettingsFileWithUiSection_ReturnsDefaults()
    {
        using var contentRoot = new TemporaryContentRoot();
        WriteFile(contentRoot, _legacyUserSettingsFileName, """{ "Ui": { "Theme": "Dark" } }""");
        using var host = contentRoot.BuildHost();

        var settings = await host.Services.GetRequiredService<IUiSettingsStore>().LoadAsync(TestContext.Current.CancellationToken);

        settings.ShouldBe(UiSettings.Default);
    }

    [Fact]
    public void BuildHost_UiSettingsFile_StaysOutOfConfiguration()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);
        WriteFile(contentRoot, GitHubBackupHostBuilder.UiSettingsFileName, """
            { "Theme": "Dark", "MainWindow": { "Width": 900 }, "Tools": { "GitPath": "D:\\Ui\\git.exe" } }
            """);

        using var host = contentRoot.BuildHost();

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        configuration["Theme"].ShouldBeNull();
        configuration.GetSection(UiSettingsFile.MainWindowKey).Exists().ShouldBeFalse();
        host.Services.GetRequiredService<IOptions<ToolsOptions>>().Value.GitPath.ShouldBe(@"C:\AppSettings\git.exe");
    }

    [Fact]
    public async Task StartAsync_InvalidUiSettingsFile_StartsAndLoadsDefaults()
    {
        using var contentRoot = new TemporaryContentRoot();
        WriteFile(contentRoot, GitHubBackupHostBuilder.UiSettingsFileName, "{ not json");
        using var host = contentRoot.BuildHost();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var settings = await host.Services.GetRequiredService<IUiSettingsStore>().LoadAsync(TestContext.Current.CancellationToken);
        settings.ShouldBe(UiSettings.Default);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SaveAsync_ToolPaths_NewHostReadsSavedPaths()
    {
        using var contentRoot = new TemporaryContentRoot();
        using (var host = contentRoot.BuildHost())
        {
            await host.Services.GetRequiredService<IToolPathsStore>()
                .SaveAsync(new(@"D:\Saved\git.exe", null), TestContext.Current.CancellationToken);
        }

        using var nextHost = contentRoot.BuildHost();

        var paths = nextHost.Services.GetRequiredService<IToolPathsStore>().Current;
        paths.GitPath.ShouldBe(@"D:\Saved\git.exe");
        string.IsNullOrEmpty(paths.SevenZipPath).ShouldBeTrue();
    }

    [Fact]
    public async Task SaveAsync_Telemetry_NewHostReadsSavedSettings()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);
        using (var host = contentRoot.BuildHost())
        {
            await host.Services.GetRequiredService<ITelemetrySettingsStore>()
                .SaveAsync(new(true, "http://saved:4317"), TestContext.Current.CancellationToken);
        }

        using var nextHost = contentRoot.BuildHost();

        nextHost.Services.GetRequiredService<ITelemetrySettingsStore>().Current.ShouldBe(new(true, "http://saved:4317"));
        nextHost.Services.GetRequiredService<IToolPathsStore>().Current.ShouldBe(new(@"C:\AppSettings\git.exe", @"C:\AppSettings\7z.exe"));
    }

    [Fact]
    public async Task SaveAsync_ManualKeysInAppSettings_KeepsThemForNextHost()
    {
        using var contentRoot = new TemporaryContentRoot("""
            {
              "backup": { "shortHashLength": 12 },
              "Serilog": { "MinimumLevel": { "Override": { "Microsoft.Hosting.Lifetime": "Information" } } },
              "tools": { "gitpath": "C:\\Manual\\git.exe" }
            }
            """);
        using (var host = contentRoot.BuildHost())
        {
            host.Services.GetRequiredService<IToolPathsStore>().Current.GitPath.ShouldBe(@"C:\Manual\git.exe");
            await host.Services.GetRequiredService<IToolPathsStore>()
                .SaveAsync(new(@"D:\Saved\git.exe", null), TestContext.Current.CancellationToken);
        }

        using var nextHost = contentRoot.BuildHost();

        nextHost.Services.GetRequiredService<IOptions<BackupOptions>>().Value.ShortHashLength.ShouldBe(12);
        nextHost.Services.GetRequiredService<IOptions<SerilogOptions>>().Value.Override[SerilogOptions.HostingLifetimeSource]
            .ShouldBe("Information");
        nextHost.Services.GetRequiredService<IToolPathsStore>().Current.GitPath.ShouldBe(@"D:\Saved\git.exe");
        File.Exists(Path.Combine(contentRoot.FolderPath, GitHubBackupHostBuilder.AppSettingsFileName + ".bak")).ShouldBeTrue();
    }

    [Fact]
    public async Task SaveAsync_UiSettings_WritesOnlyUiSettingsFile()
    {
        using var contentRoot = new TemporaryContentRoot(_appSettings);
        var saved = new UiSettings(UiTheme.Dark, new(10, 20, 1000, 700, true), 200, 300);
        using (var host = contentRoot.BuildHost())
        {
            await host.Services.GetRequiredService<IUiSettingsStore>().SaveAsync(saved, TestContext.Current.CancellationToken);
        }

        File.ReadAllText(Path.Combine(contentRoot.FolderPath, GitHubBackupHostBuilder.AppSettingsFileName)).ShouldBe(_appSettings);
        File.Exists(Path.Combine(contentRoot.FolderPath, _legacyUserSettingsFileName)).ShouldBeFalse();
        using var nextHost = contentRoot.BuildHost();
        var settings = await nextHost.Services.GetRequiredService<IUiSettingsStore>().LoadAsync(TestContext.Current.CancellationToken);
        settings.ShouldBe(saved);
    }

    [Fact]
    public async Task SaveAsync_ToolPaths_DoesNotCreateOtherSettingsFiles()
    {
        using var contentRoot = new TemporaryContentRoot();
        using var host = contentRoot.BuildHost();

        await host.Services.GetRequiredService<IToolPathsStore>()
            .SaveAsync(new(@"D:\Saved\git.exe", null), TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(contentRoot.FolderPath, GitHubBackupHostBuilder.UiSettingsFileName)).ShouldBeFalse();
        File.Exists(Path.Combine(contentRoot.FolderPath, _legacyUserSettingsFileName)).ShouldBeFalse();
    }

    /// <summary>
    /// Writes <paramref name="json"/> as the file <paramref name="fileName"/> of <paramref name="contentRoot"/>.
    /// </summary>
    /// <param name="contentRoot">The content root of the host.</param>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="json">The JSON text of the file.</param>
    private static void WriteFile(TemporaryContentRoot contentRoot, string fileName, string json) =>
        File.WriteAllText(Path.Combine(contentRoot.FolderPath, fileName), json);
}
