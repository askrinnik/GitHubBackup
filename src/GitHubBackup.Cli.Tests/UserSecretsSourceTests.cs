using GitHubBackup.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests the user-secrets configuration source of the console host: it is read only in the Development environment
/// and ranks above <c>appsettings.json</c> and below prefixed environment variables and command-line arguments.
/// The tests point <c>APPDATA</c> at a temporary folder, so the real user profile is never read or written.
/// </summary>
[Collection(ProcessEnvironmentTestGroup.Name)]
public sealed class UserSecretsSourceTests : IDisposable
{
    /// <summary>The user-secrets id of the console host.</summary>
    internal const string UserSecretsId = "GitHubBackup";

    private readonly string? _previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? _previousPrefixedValue =
        Environment.GetEnvironmentVariable(EnvironmentVariableOverrideTests.PrefixedVariable);
    private readonly TemporaryContentRoot _contentRoot = new();
    private readonly TemporaryContentRoot _appData = new();

    /// <summary>
    /// Redirects <c>APPDATA</c> to the temporary folder and clears the prefixed variable.
    /// </summary>
    public UserSecretsSourceTests()
    {
        Environment.SetEnvironmentVariable("APPDATA", _appData.FolderPath);
        Environment.SetEnvironmentVariable(EnvironmentVariableOverrideTests.PrefixedVariable, null);
    }

    [Fact]
    public void BuildHost_DevelopmentWithUserSecret_OverridesAppSettings()
    {
        WriteUserSecrets(14);

        ShortHashLength(Environments.Development).ShouldBe(14);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void BuildHost_NotDevelopmentWithUserSecret_IgnoresUserSecrets(string environmentName)
    {
        WriteUserSecrets(14);

        ShortHashLength(environmentName).ShouldBe(10);
    }

    [Fact]
    public void BuildHost_DevelopmentWithoutSecretsFile_UsesAppSettings() =>
        ShortHashLength(Environments.Development).ShouldBe(10);

    [Fact]
    public void BuildHost_PrefixedVariable_OverridesUserSecret()
    {
        WriteUserSecrets(14);
        Environment.SetEnvironmentVariable(EnvironmentVariableOverrideTests.PrefixedVariable, "12");

        ShortHashLength(Environments.Development).ShouldBe(12);
    }

    [Fact]
    public void BuildHost_CommandLineArgument_OverridesUserSecret()
    {
        WriteUserSecrets(14);

        ShortHashLength(Environments.Development, "--Backup:ShortHashLength=20").ShouldBe(20);
    }

    /// <summary>
    /// Restores the variables the tests change and deletes the temporary folders.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", _previousAppData);
        Environment.SetEnvironmentVariable(EnvironmentVariableOverrideTests.PrefixedVariable, _previousPrefixedValue);
        _appData.Dispose();
        _contentRoot.Dispose();
    }

    /// <summary>
    /// Writes a <c>secrets.json</c> that sets <c>Backup:ShortHashLength</c> into the user-secrets store under the
    /// temporary <c>APPDATA</c>.
    /// </summary>
    /// <param name="shortHashLength">The value to store.</param>
    private void WriteUserSecrets(int shortHashLength)
    {
        var folder = Path.Combine(_appData.FolderPath, "Microsoft", "UserSecrets", UserSecretsId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "secrets.json"),
            $$"""{ "Backup:ShortHashLength": "{{shortHashLength}}" }""");
    }

    /// <summary>
    /// Builds the console host in <paramref name="environmentName"/> over the valid settings file and returns the bound
    /// <c>Backup:ShortHashLength</c>.
    /// </summary>
    /// <param name="environmentName">The hosting environment name.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The bound value.</returns>
    private int ShortHashLength(string environmentName, params string[] args)
    {
        var baseSettings = CliApplicationTests.CreateSettings(
            _contentRoot.FolderPath, CliApplicationTests.ValidAppSettings, args);
        var settings = baseSettings with { EnvironmentName = environmentName };
        using var host = CliApplication.BuildHost(settings);
        return host.Services.GetRequiredService<IOptions<BackupOptions>>().Value.ShortHashLength;
    }
}
