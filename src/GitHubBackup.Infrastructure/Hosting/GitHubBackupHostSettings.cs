using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Describes the process a host is built for: its configuration arguments, its folder and its environment.
/// </summary>
public sealed record GitHubBackupHostSettings
{
    /// <summary>The environment variable that names the host environment.</summary>
    public const string EnvironmentVariableName = "DOTNET_ENVIRONMENT";

    /// <summary>
    /// The environment variable that names the Windows Credential Manager target of the OTLP headers in place of
    /// <see cref="DefaultOtlpHeadersCredentialTarget"/>, so that tests that start an executable never touch the
    /// credential of the user.
    /// </summary>
    /// <remarks>
    /// The name lies outside <see cref="GitHubBackupHostBuilder.EnvironmentVariablePrefix"/>, so the variable never
    /// becomes a configuration key. Only a target that starts with <see cref="TestOtlpHeadersCredentialTargetPrefix"/>
    /// is taken, so a stray value cannot point the headers at another credential, such as the GitHub token.
    /// </remarks>
    public const string OtlpHeadersCredentialTargetVariableName = "GITHUBBACKUP_OTLP_HEADERS_CREDENTIAL_TARGET";

    /// <summary>The prefix every target named by <see cref="OtlpHeadersCredentialTargetVariableName"/> must have.</summary>
    public const string TestOtlpHeadersCredentialTargetPrefix = "GitHubBackup.Tests:";

    /// <summary>The Windows Credential Manager target that holds the OTLP headers of the application.</summary>
    public const string DefaultOtlpHeadersCredentialTarget = CredentialManagerOtlpHeadersStore.DefaultTarget;

    /// <summary>
    /// Gets the configuration keys given on the command line, each in the form <c>--Section:Key=value</c>; the host
    /// reads them as configuration and does not interpret any other argument.
    /// </summary>
    public IReadOnlyList<string> ConfigurationArguments { get; init; } = [];

    /// <summary>
    /// Gets the absolute path of <c>backup-config.json</c> that overrides <c>Backup:ConfigPath</c>, or
    /// <see langword="null"/> to keep the configured path.
    /// </summary>
    public string? BackupConfigPath { get; init; }

    /// <summary>
    /// Gets the absolute path of an existing folder that holds the executable; relative paths in the configuration
    /// are resolved against it.
    /// </summary>
    public required string ContentRootPath { get; init; }

    /// <summary>Gets the host environment name, for example <see cref="Environments.Production"/>.</summary>
    public required string EnvironmentName { get; init; }

    /// <summary>
    /// Gets the file provider that supplies <c>appsettings.json</c>; <see langword="null"/> reads it from
    /// <see cref="ContentRootPath"/>.
    /// </summary>
    public IFileProvider? ConfigurationFileProvider { get; init; }

    /// <summary>
    /// Gets the Windows Credential Manager target that holds the OTLP headers: the host reads it as a configuration
    /// source and the settings editor and <c>set-otlp-headers</c> write it. <see langword="null"/> builds a host
    /// without that source, whose header store keeps nothing.
    /// </summary>
    public string? OtlpHeadersCredentialTarget { get; init; }

    /// <summary>
    /// Creates the settings of the current process: the folder of the executable and the environment named by
    /// <see cref="EnvironmentVariableName"/>, <see cref="Environments.Production"/> when it is not set, and the OTLP
    /// headers credential that <see cref="ResolveOtlpHeadersCredentialTarget"/> picks.
    /// </summary>
    /// <returns>The settings of the current process.</returns>
    public static GitHubBackupHostSettings ForCurrentProcess()
    {
        var environmentName = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return new()
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? Environments.Production : environmentName,
            OtlpHeadersCredentialTarget = ResolveOtlpHeadersCredentialTarget(
                Environment.GetEnvironmentVariable(OtlpHeadersCredentialTargetVariableName)),
        };
    }

    /// <summary>
    /// Picks the OTLP headers credential target from the value of
    /// <see cref="OtlpHeadersCredentialTargetVariableName"/>.
    /// </summary>
    /// <param name="variableValue">The value of the variable, or <see langword="null"/> when it is not set.</param>
    /// <returns>
    /// The value when it starts with <see cref="TestOtlpHeadersCredentialTargetPrefix"/>; otherwise
    /// <see cref="DefaultOtlpHeadersCredentialTarget"/>.
    /// </returns>
    internal static string ResolveOtlpHeadersCredentialTarget(string? variableValue) =>
        variableValue is not null
        && variableValue.Length > TestOtlpHeadersCredentialTargetPrefix.Length
        && variableValue.StartsWith(TestOtlpHeadersCredentialTargetPrefix, StringComparison.Ordinal)
            ? variableValue
            : DefaultOtlpHeadersCredentialTarget;
}
