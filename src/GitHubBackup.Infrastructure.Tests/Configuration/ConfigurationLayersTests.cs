using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="ConfigurationLayers"/> over configurations built in the order of the host: the settings file,
/// Windows Credential Manager, the environment variables, the command line.
/// </summary>
public sealed class ConfigurationLayersTests
{
    private const string _key = "OpenTelemetry:Headers";

    [Fact]
    public void FindHeadersLayers_OnlyFile_ReturnsFileValueNotOverridden()
    {
        var configuration = CreateConfiguration("""{ "OpenTelemetry": { "Headers": "api-key=file" } }""");

        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe(("api-key=file", null, false));
    }

    [Fact]
    public void FindHeadersLayers_CommandLineSetsKey_ReturnsFileValueAndOverridden()
    {
        var configuration = CreateConfiguration(
            """{ "OpenTelemetry": { "Headers": "api-key=file" } }""",
            "--OpenTelemetry:Headers=api-key=cli");

        configuration[_key].ShouldBe("api-key=cli");
        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe(("api-key=file", null, true));
    }

    [Fact]
    public void FindHeadersLayers_EnvironmentSetsKeyWithoutFileValue_ReturnsNullAndOverridden()
    {
        var prefix = $"GitHubBackupLayersTest{Guid.NewGuid():N}__";
        var variable = prefix + "OpenTelemetry__Headers";
        Environment.SetEnvironmentVariable(variable, "api-key=environment");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(InMemoryFileProvider.WithAppSettings("{}"), GitHubBackupHostBuilder.AppSettingsFileName, optional: true, reloadOnChange: false)
                .AddEnvironmentVariables(prefix)
                .Build();

            ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe((null, null, true));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void FindHeadersLayers_KeyInOtherCase_IsFound()
    {
        var configuration = CreateConfiguration(
            """{ "openTelemetry": { "headers": "api-key=file" } }""",
            "--OPENTELEMETRY:HEADERS=api-key=cli");

        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe(("api-key=file", null, true));
    }

    [Fact]
    public void FindHeadersLayers_NoFileProvider_ReturnsNoFileValueAndOverridden()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new(_key, "api-key=memory")])
            .Build();

        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe((null, null, true));
    }

    [Fact]
    public void FindHeadersLayers_NoFileProviderAndNoValue_ReturnsNotOverridden()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe((null, null, false));
    }

    [Fact]
    public void FindHeadersLayers_EmptyOverride_IsOverridden()
    {
        var configuration = CreateConfiguration(
            """{ "OpenTelemetry": { "Headers": "api-key=file" } }""",
            "--OpenTelemetry:Headers=");

        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe(("api-key=file", null, true));
    }

    [Fact]
    public void FindHeadersLayers_NoFile_ReturnsNullNotOverridden()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(new InMemoryFileProvider(new Dictionary<string, string>()), GitHubBackupHostBuilder.AppSettingsFileName, optional: true, reloadOnChange: false)
            .Build();

        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe((null, null, false));
    }

    [Fact]
    public void FindHeadersLayers_OnlyCredential_ReturnsCredentialValueNotOverridden()
    {
        var configuration = CreateConfigurationWithCredential("{}", "api-key=credential");

        configuration[_key].ShouldBe("api-key=credential");
        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe((null, "api-key=credential", false));
    }

    [Fact]
    public void FindHeadersLayers_CredentialAndFile_ReturnsBothNotOverridden()
    {
        var configuration = CreateConfigurationWithCredential(
            """{ "OpenTelemetry": { "Headers": "api-key=file" } }""", "api-key=credential");

        configuration[_key].ShouldBe("api-key=credential");
        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe(("api-key=file", "api-key=credential", false));
    }

    [Fact]
    public void FindHeadersLayers_CommandLineOverCredential_ReturnsCredentialValueAndOverridden()
    {
        var configuration = CreateConfigurationWithCredential("{}", "api-key=credential", "--OpenTelemetry:Headers=api-key=cli");

        configuration[_key].ShouldBe("api-key=cli");
        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe((null, "api-key=credential", true));
    }

    [Fact]
    public void FindHeadersLayers_EnvironmentOverCredential_IsOverridden()
    {
        var prefix = $"GitHubBackupLayersTest{Guid.NewGuid():N}__";
        var variable = prefix + "OpenTelemetry__Headers";
        Environment.SetEnvironmentVariable(variable, "api-key=environment");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(InMemoryFileProvider.WithAppSettings("{}"), GitHubBackupHostBuilder.AppSettingsFileName, optional: true, reloadOnChange: false)
                .Add(new CredentialManagerConfigurationSource(() => "api-key=credential", new ConfigurationWarnings()))
                .AddEnvironmentVariables(prefix)
                .Build();

            ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe((null, "api-key=credential", true));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void FindHeadersLayers_UserSecretsOverCredential_IsOverridden()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(InMemoryFileProvider.WithAppSettings("{}"), GitHubBackupHostBuilder.AppSettingsFileName, optional: true, reloadOnChange: false)
            .Add(new CredentialManagerConfigurationSource(() => "api-key=credential", new ConfigurationWarnings()))
            .AddJsonFile(
                new InMemoryFileProvider(new Dictionary<string, string>
                {
                    ["secrets.json"] = """{ "OpenTelemetry": { "Headers": "api-key=secrets" } }""",
                }),
                "secrets.json",
                optional: false,
                reloadOnChange: false)
            .Build();

        ConfigurationLayers.FindHeadersLayers(configuration).ShouldBe((null, "api-key=credential", true));
    }

    /// <summary>
    /// Builds a configuration from <paramref name="appSettings"/> as the settings file and
    /// <paramref name="arguments"/> as the command line.
    /// </summary>
    /// <param name="appSettings">The JSON text of the settings file.</param>
    /// <param name="arguments">The command-line arguments.</param>
    /// <returns>The configuration.</returns>
    private static IConfigurationRoot CreateConfiguration(string appSettings, params string[] arguments) =>
        new ConfigurationBuilder()
            .AddJsonFile(InMemoryFileProvider.WithAppSettings(appSettings), GitHubBackupHostBuilder.AppSettingsFileName, optional: true, reloadOnChange: false)
            .AddCommandLine(arguments)
            .Build();

    /// <summary>
    /// Builds a configuration from <paramref name="appSettings"/> as the settings file, <paramref name="credential"/>
    /// as the headers in Windows Credential Manager and <paramref name="arguments"/> as the command line.
    /// </summary>
    /// <param name="appSettings">The JSON text of the settings file.</param>
    /// <param name="credential">The saved headers, or <see langword="null"/> for none.</param>
    /// <param name="arguments">The command-line arguments.</param>
    /// <returns>The configuration.</returns>
    private static IConfigurationRoot CreateConfigurationWithCredential(
        string appSettings, string? credential, params string[] arguments) =>
        new ConfigurationBuilder()
            .AddJsonFile(InMemoryFileProvider.WithAppSettings(appSettings), GitHubBackupHostBuilder.AppSettingsFileName, optional: true, reloadOnChange: false)
            .Add(new CredentialManagerConfigurationSource(() => credential, new ConfigurationWarnings()))
            .AddCommandLine(arguments)
            .Build();
}
