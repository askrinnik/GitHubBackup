using GitHubBackup.Core;
using GitHubBackup.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests the priority of environment variables among the configuration sources of the console host.
/// </summary>
[Collection(ProcessEnvironmentTestGroup.Name)]
public sealed class EnvironmentVariableOverrideTests : IDisposable
{
    /// <summary>The prefixed variable that sets <c>Backup:ShortHashLength</c>.</summary>
    internal const string PrefixedVariable = "GitHubBackup__Backup__ShortHashLength";

    /// <summary>The same key without the application prefix, which the host must ignore.</summary>
    internal const string UnprefixedVariable = "Backup__ShortHashLength";

    private readonly string? _previousPrefixedValue = Environment.GetEnvironmentVariable(PrefixedVariable);
    private readonly string? _previousUnprefixedValue = Environment.GetEnvironmentVariable(UnprefixedVariable);
    private readonly TemporaryContentRoot _contentRoot = new();

    [Fact]
    public void BuildHost_PrefixedVariable_OverridesAppSettings()
    {
        Environment.SetEnvironmentVariable(PrefixedVariable, "12");

        ShortHashLength().ShouldBe(12);
    }

    [Fact]
    public void BuildHost_CommandLineArgument_OverridesPrefixedVariable()
    {
        Environment.SetEnvironmentVariable(PrefixedVariable, "12");

        ShortHashLength("--Backup:ShortHashLength=20").ShouldBe(20);
    }

    [Fact]
    public void BuildHost_UnprefixedVariable_IsIgnored()
    {
        Environment.SetEnvironmentVariable(PrefixedVariable, null);
        Environment.SetEnvironmentVariable(UnprefixedVariable, "13");

        ShortHashLength().ShouldBe(10);
    }

    [Fact]
    public async Task RunAsync_InvalidPrefixedVariable_ReturnsCritical()
    {
        Environment.SetEnvironmentVariable(PrefixedVariable, "41");
        using var standardError = new StringWriter();

        var exitCode = await new CliApplication(standardError).RunAsync(
            CliApplicationTests.CreateSettings(_contentRoot.FolderPath, CliApplicationTests.ValidAppSettings),
            TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        standardError.ToString().ShouldContain("Backup:ShortHashLength");
    }

    /// <summary>
    /// Restores the variables the tests change and deletes the content root.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PrefixedVariable, _previousPrefixedValue);
        Environment.SetEnvironmentVariable(UnprefixedVariable, _previousUnprefixedValue);
        _contentRoot.Dispose();
    }

    /// <summary>
    /// Builds the console host over the valid settings file and returns the bound <c>Backup:ShortHashLength</c>.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The bound value.</returns>
    private int ShortHashLength(params string[] args)
    {
        using var host = CliApplication.BuildHost(
            CliApplicationTests.CreateSettings(_contentRoot.FolderPath, CliApplicationTests.ValidAppSettings, args));
        return host.Services.GetRequiredService<IOptions<BackupOptions>>().Value.ShortHashLength;
    }
}
