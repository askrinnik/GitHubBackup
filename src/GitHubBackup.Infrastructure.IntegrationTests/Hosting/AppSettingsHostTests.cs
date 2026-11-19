using System.ComponentModel;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Security;
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
    /// <summary>
    /// The Win32 error a process without a logon session gets from Credential Manager, as on some CI runners.
    /// </summary>
    private const int _noSuchLogonSession = 1312;

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
                .SaveAsync(new(true, "http://saved:4317", null), TestContext.Current.CancellationToken);
        }

        using var nextHost = contentRoot.BuildHost();

        nextHost.Services.GetRequiredService<ITelemetrySettingsStore>().Current.ShouldBe(new(true, "http://saved:4317", null));
        nextHost.Services.GetRequiredService<IToolPathsStore>().Current.ShouldBe(new(@"C:\AppSettings\git.exe", @"C:\AppSettings\7z.exe"));
    }

    [Fact]
    public void BuildHost_HeadersOnCommandLine_TelemetryStoreReportsOverridden()
    {
        using var contentRoot = new TemporaryContentRoot("""{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""");

        using var host = contentRoot.BuildHost("--OpenTelemetry:Headers=api-key=cli-secret");

        var store = host.Services.GetRequiredService<ITelemetrySettingsStore>();
        store.Current.Headers.ShouldBe("api-key=file-secret");
        store.HeadersOverridden.ShouldBeTrue();
        host.Services.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value.Headers.ShouldBe("api-key=cli-secret");
        var masker = host.Services.GetRequiredService<ISecretMasker>();
        masker.Mask("Sent file-secret and cli-secret.").ShouldBe("Sent *** and ***.");
    }

    [Fact]
    public async Task SaveAsync_TelemetryHeadersInAppSettings_MovesThemToCredentialManager()
    {
        using var credential = new OtlpHeadersTestCredential();
        using var contentRoot = new TemporaryContentRoot(
            """{ "OpenTelemetry": { "Enabled": false, "Headers": "api-key=file-secret" } }""", credential.Target);
        using (var host = contentRoot.BuildHost())
        {
            var store = host.Services.GetRequiredService<ITelemetrySettingsStore>();
            store.HeadersInAppSettings.ShouldBeTrue();
            store.Current.Headers.ShouldBe("api-key=file-secret");
            await SaveOrSkipAsync(store, new(false, null, "api-key=saved-secret"));
        }

        using var nextHost = contentRoot.BuildHost();

        credential.Read().ShouldBe("api-key=saved-secret");
        var appSettingsPath = Path.Combine(contentRoot.FolderPath, GitHubBackupHostBuilder.AppSettingsFileName);
        File.ReadAllText(appSettingsPath).ShouldNotContain("Headers");
        File.ReadAllText(appSettingsPath + ".bak").ShouldContain("file-secret");
        var nextStore = nextHost.Services.GetRequiredService<ITelemetrySettingsStore>();
        nextStore.Current.Headers.ShouldBe("api-key=saved-secret");
        nextStore.HeadersOverridden.ShouldBeFalse();
        nextStore.HeadersInAppSettings.ShouldBeFalse();
        nextHost.Services.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value.Headers.ShouldBe("api-key=saved-secret");
    }

    [Fact]
    public async Task SaveAsync_BlankTelemetryHeaders_DeletesCredential()
    {
        using var credential = new OtlpHeadersTestCredential();
        WriteOrSkip(credential, "api-key=credential-secret");
        using var contentRoot = new TemporaryContentRoot(otlpHeadersCredentialTarget: credential.Target);
        using (var host = contentRoot.BuildHost())
        {
            await SaveOrSkipAsync(host.Services.GetRequiredService<ITelemetrySettingsStore>(), new(false, null, " "));
        }

        credential.Read().ShouldBeNull();
        using var nextHost = contentRoot.BuildHost();
        nextHost.Services.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value.Headers.ShouldBeNull();
    }

    [Fact]
    public void BuildHost_CredentialHeaders_OverrideFileAndAreMasked()
    {
        using var credential = new OtlpHeadersTestCredential();
        WriteOrSkip(credential, "api-key=credential-secret");
        using var contentRoot = new TemporaryContentRoot(
            """{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""", credential.Target);

        using var host = contentRoot.BuildHost();

        host.Services.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value.Headers.ShouldBe("api-key=credential-secret");
        var store = host.Services.GetRequiredService<ITelemetrySettingsStore>();
        store.Current.Headers.ShouldBe("api-key=credential-secret");
        store.HeadersOverridden.ShouldBeFalse();
        store.HeadersInAppSettings.ShouldBeTrue();
        host.Services.GetRequiredService<ISecretMasker>()
            .Mask("Sent credential-secret and file-secret.").ShouldBe("Sent *** and ***.");
    }

    [Fact]
    public void BuildHost_CommandLineHeaders_OverrideCredential()
    {
        using var credential = new OtlpHeadersTestCredential();
        WriteOrSkip(credential, "api-key=credential-secret");
        using var contentRoot = new TemporaryContentRoot(otlpHeadersCredentialTarget: credential.Target);

        using var host = contentRoot.BuildHost("--OpenTelemetry:Headers=api-key=cli-secret");

        host.Services.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value.Headers.ShouldBe("api-key=cli-secret");
        var store = host.Services.GetRequiredService<ITelemetrySettingsStore>();
        store.Current.Headers.ShouldBe("api-key=credential-secret");
        store.HeadersOverridden.ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_MalformedCredentialHeadersWhileEnabled_FailsValidationWithoutValue()
    {
        using var credential = new OtlpHeadersTestCredential();
        WriteOrSkip(credential, "api-key credential-secret");
        using var contentRoot = new TemporaryContentRoot(
            """{ "OpenTelemetry": { "Enabled": true, "OtlpEndpoint": "https://otlp.example:4317" } }""",
            credential.Target);
        using var host = contentRoot.BuildHost();

        var exception = await Should.ThrowAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(OpenTelemetryOptions.HeadersKey);
        exception.Message.ShouldNotContain("credential-secret");
    }

    [Fact]
    public void BuildHost_NoCredentialTarget_HasNoCredentialManagerSource()
    {
        using var contentRoot = new TemporaryContentRoot();

        using var host = contentRoot.BuildHost();

        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();
        configuration.Providers.OfType<CredentialManagerConfigurationProvider>().ShouldBeEmpty();
    }

    [Fact]
    public void BuildHost_CredentialTarget_AddsCredentialManagerSourceAfterAppSettings()
    {
        using var contentRoot = new TemporaryContentRoot(otlpHeadersCredentialTarget: OtlpHeadersTestCredential.CreateTarget());

        using var host = contentRoot.BuildHost();

        var providers = ((IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>()).Providers.ToList();
        var fileIndex = providers.FindIndex(provider => provider is FileConfigurationProvider);
        var credentialIndex = providers.FindIndex(provider => provider is CredentialManagerConfigurationProvider);
        credentialIndex.ShouldBe(fileIndex + 1);
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

    /// <summary>
    /// Saves <paramref name="credential"/>, or skips the test when the process has no logon session to keep
    /// credentials in.
    /// </summary>
    /// <param name="credential">The test credential.</param>
    /// <param name="headers">The headers to save.</param>
    private static void WriteOrSkip(OtlpHeadersTestCredential credential, string headers)
    {
        try
        {
            credential.Write(headers);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == _noSuchLogonSession)
        {
            Assert.Skip("Windows Credential Manager is not available without a logon session.");
        }
    }

    /// <summary>
    /// Saves <paramref name="settings"/> through <paramref name="store"/>, or skips the test when the process has no
    /// logon session to keep credentials in.
    /// </summary>
    /// <param name="store">The telemetry settings store of a host.</param>
    /// <param name="settings">The settings to save.</param>
    /// <returns>A task that completes when the settings are saved.</returns>
    private static async Task SaveOrSkipAsync(ITelemetrySettingsStore store, TelemetrySettings settings)
    {
        try
        {
            await store.SaveAsync(settings, TestContext.Current.CancellationToken);
        }
        catch (OtlpHeadersStoreException exception)
            when (exception.InnerException is Win32Exception { NativeErrorCode: _noSuchLogonSession })
        {
            Assert.Skip("Windows Credential Manager is not available without a logon session.");
        }
    }
}
