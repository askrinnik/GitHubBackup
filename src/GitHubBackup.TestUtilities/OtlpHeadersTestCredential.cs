using System.ComponentModel;
using GitHubBackup.Infrastructure.Hosting;
using Meziantou.Framework.Win32;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Names a unique Windows Credential Manager target for the OTLP headers of one test and deletes the credential on
/// dispose, so that no test reads or writes the credential of the user.
/// </summary>
public sealed class OtlpHeadersTestCredential : IDisposable
{
    /// <summary>Gets the name of the credential.</summary>
    public string Target { get; } = CreateTarget();

    /// <summary>
    /// Returns a target name that no other test and no installation of the application uses.
    /// </summary>
    /// <returns>The target name.</returns>
    public static string CreateTarget() =>
        $"{GitHubBackupHostSettings.TestOtlpHeadersCredentialTargetPrefix}{Guid.NewGuid():N}:OpenTelemetry:Headers";

    /// <summary>
    /// Adds the variable that points an executable of the application at <see cref="Target"/> to
    /// <paramref name="environment"/>.
    /// </summary>
    /// <param name="environment">The environment variables of the child process.</param>
    public void AddTo(IDictionary<string, string> environment) =>
        environment[GitHubBackupHostSettings.OtlpHeadersCredentialTargetVariableName] = Target;

    /// <summary>
    /// Reads the credential.
    /// </summary>
    /// <returns>The saved headers, or <see langword="null"/> when there is no credential.</returns>
    public string? Read() => CredentialManager.ReadCredential(Target, CredentialType.Generic)?.Password;

    /// <summary>
    /// Saves <paramref name="headers"/> as the credential.
    /// </summary>
    /// <param name="headers">The header list.</param>
    public void Write(string headers) => CredentialManager.WriteCredential(
        Target, "GitHubBackup", headers, CredentialPersistence.LocalMachine, CredentialType.Generic);

    /// <summary>
    /// Deletes the credential when it exists.
    /// </summary>
    /// <remarks>
    /// A process without a logon session cannot reach Credential Manager at all; it has saved nothing, so the failure
    /// is ignored.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            CredentialManager.TryDeleteCredential(Target, CredentialType.Generic);
        }
        catch (Win32Exception)
        {
            // Without a logon session nothing was saved, so there is nothing to delete.
        }
    }
}
