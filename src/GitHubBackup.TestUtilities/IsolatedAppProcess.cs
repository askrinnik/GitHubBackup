using System.Diagnostics;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Builds the start information of an executable of the application in a temporary installation, isolated from the
/// configuration of the machine it runs on.
/// </summary>
public static class IsolatedAppProcess
{
    /// <summary>The prefix of the environment variables that configure git.</summary>
    private const string _gitVariablePrefix = "GIT_";

    /// <summary>
    /// Returns the start information of <paramref name="executablePath"/> in <paramref name="workingDirectory"/>.
    /// </summary>
    /// <remarks>
    /// The process gets the environment of the caller without the variables that configure the application or git,
    /// so that a value set on the developer's machine cannot change the scenario, and with the Production
    /// environment, which skips the user secrets of the developer that could supply another token or API address.
    /// <paramref name="environment"/> is applied last.
    /// </remarks>
    /// <param name="executablePath">The absolute path of the executable.</param>
    /// <param name="workingDirectory">The folder the process starts in, usually the installation folder.</param>
    /// <param name="environment">More environment variables of the process.</param>
    /// <returns>The start information, without the shell and with the given environment.</returns>
    public static ProcessStartInfo Create(
        string executablePath, string workingDirectory, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        foreach (var name in startInfo.Environment.Keys.Where(IsInheritedConfiguration).ToList())
        {
            startInfo.Environment.Remove(name);
        }

        startInfo.Environment[GitHubBackupHostSettings.EnvironmentVariableName] = Environments.Production;
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    /// <summary>
    /// Returns whether <paramref name="name"/> configures the application or git.
    /// </summary>
    /// <param name="name">The name of an environment variable.</param>
    /// <returns><see langword="true"/> for a <c>GitHubBackup__</c> or a <c>GIT_</c> variable.</returns>
    private static bool IsInheritedConfiguration(string name) =>
        name.StartsWith(GitHubBackupHostBuilder.EnvironmentVariablePrefix, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(_gitVariablePrefix, StringComparison.OrdinalIgnoreCase);
}
