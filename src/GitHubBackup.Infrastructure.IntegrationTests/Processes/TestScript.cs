using GitHubBackup.Core.Processes;

namespace GitHubBackup.Infrastructure.IntegrationTests.Processes;

/// <summary>
/// Writes batch scripts into a test folder and builds the requests that run them with the Windows command processor.
/// </summary>
internal static class TestScript
{
    /// <summary>Gets the path of <c>cmd.exe</c> in the Windows system folder.</summary>
    public static string CommandProcessorPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    /// <summary>
    /// Writes <paramref name="lines"/> as a batch script into <paramref name="folder"/> and returns a request that runs
    /// it there with <paramref name="arguments"/>.
    /// </summary>
    /// <remarks>
    /// The script is passed by a path relative to the working directory, so the path of the folder never needs
    /// quoting on the command line of <c>cmd.exe</c>; the <c>.\</c> prefix finds it even when
    /// <c>NoDefaultCurrentDirectoryInExePath</c> keeps <c>cmd.exe</c> from searching the current directory.
    /// </remarks>
    /// <param name="folder">The folder of the script, also the working directory of the process.</param>
    /// <param name="lines">The commands of the script; echo is turned off before them.</param>
    /// <param name="arguments">The arguments of the script.</param>
    /// <returns>The request.</returns>
    public static ProcessRequest Create(string folder, IEnumerable<string> lines, params string[] arguments)
    {
        var fileName = $"script-{Guid.NewGuid():N}.cmd";
        File.WriteAllLines(Path.Combine(folder, fileName), ["@echo off", .. lines]);

        return new()
        {
            FileName = CommandProcessorPath,
            Arguments = ["/d", "/c", @".\" + fileName, .. arguments],
            WorkingDirectory = folder,
        };
    }
}
