using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="ConfigurationLayers"/> over configurations built in the order of the host: the settings file, the
/// environment variables, the command line.
/// </summary>
public sealed class ConfigurationLayersTests
{
    private const string _key = "OpenTelemetry:Headers";

    [Fact]
    public void FindAppSettingsValue_OnlyFile_ReturnsFileValueNotOverridden()
    {
        var configuration = CreateConfiguration("""{ "OpenTelemetry": { "Headers": "api-key=file" } }""");

        ConfigurationLayers.FindAppSettingsValue(configuration, _key).ShouldBe(("api-key=file", false));
    }

    [Fact]
    public void FindAppSettingsValue_CommandLineSetsKey_ReturnsFileValueAndOverridden()
    {
        var configuration = CreateConfiguration(
            """{ "OpenTelemetry": { "Headers": "api-key=file" } }""",
            "--OpenTelemetry:Headers=api-key=cli");

        configuration[_key].ShouldBe("api-key=cli");
        ConfigurationLayers.FindAppSettingsValue(configuration, _key).ShouldBe(("api-key=file", true));
    }

    [Fact]
    public void FindAppSettingsValue_EnvironmentSetsKeyWithoutFileValue_ReturnsNullAndOverridden()
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

            ConfigurationLayers.FindAppSettingsValue(configuration, _key).ShouldBe((null, true));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void FindAppSettingsValue_KeyInOtherCase_IsFound()
    {
        var configuration = CreateConfiguration(
            """{ "openTelemetry": { "headers": "api-key=file" } }""",
            "--OPENTELEMETRY:HEADERS=api-key=cli");

        ConfigurationLayers.FindAppSettingsValue(configuration, _key).ShouldBe(("api-key=file", true));
    }

    [Fact]
    public void FindAppSettingsValue_NoFileProvider_ReturnsNoFileValueAndOverridden()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new(_key, "api-key=memory")])
            .Build();

        ConfigurationLayers.FindAppSettingsValue(configuration, _key).ShouldBe((null, true));
    }

    [Fact]
    public void FindAppSettingsValue_NoFileProviderAndNoValue_ReturnsNotOverridden()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        ConfigurationLayers.FindAppSettingsValue(configuration, _key).ShouldBe((null, false));
    }

    [Fact]
    public void FindAppSettingsValue_EmptyOverride_IsOverridden()
    {
        var configuration = CreateConfiguration(
            """{ "OpenTelemetry": { "Headers": "api-key=file" } }""",
            "--OpenTelemetry:Headers=");

        ConfigurationLayers.FindAppSettingsValue(configuration, _key).ShouldBe(("api-key=file", true));
    }

    [Fact]
    public void FindAppSettingsValue_NoFile_ReturnsNullNotOverridden()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(new InMemoryFileProvider(new Dictionary<string, string>()), GitHubBackupHostBuilder.AppSettingsFileName, optional: true, reloadOnChange: false)
            .Build();

        ConfigurationLayers.FindAppSettingsValue(configuration, _key).ShouldBe((null, false));
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
}
