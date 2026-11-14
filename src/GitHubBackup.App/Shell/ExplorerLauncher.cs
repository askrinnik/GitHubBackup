using System.IO;
using GitHubBackup.Core.Processes;

namespace GitHubBackup.App.Shell;

/// <summary>
/// Starts <c>explorer.exe</c> from the Windows folder through <see cref="IProcessRunner"/>.
/// </summary>
/// <remarks>
/// The exit code is ignored: <c>explorer.exe</c> hands the request to the running shell and exits with <c>1</c> even
/// when the window opens.
/// </remarks>
/// <param name="processRunner">Starts the process and logs it.</param>
internal sealed class ExplorerLauncher(IProcessRunner processRunner) : IExplorerLauncher
{
    /// <summary>The Windows folder, which holds <c>explorer.exe</c> and serves as the working directory.</summary>
    private readonly string _windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="folder"/> contains a comma.</exception>
    public Task OpenFolderAsync(string folder, CancellationToken cancellationToken)
    {
        // Explorer splits its whole command line on commas, also inside quotes, and opens every part as a separate
        // target; a comma in the path could make it start a program named after the comma.
        if (folder.Contains(','))
        {
            throw new ArgumentException($"Explorer cannot open the folder '{folder}': the path contains a comma.", nameof(folder));
        }

        return RunAsync([folder], cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A path with a comma cannot be passed to <c>/select</c> safely, so Explorer opens the containing folder instead.
    /// </remarks>
    /// <exception cref="ArgumentException">The containing folder contains a comma.</exception>
    public Task ShowFileAsync(string filePath, CancellationToken cancellationToken) =>
        filePath.Contains(',')
            ? OpenFolderAsync(Path.GetDirectoryName(filePath) ?? filePath, cancellationToken)
            : RunAsync(["/select," + filePath], cancellationToken);

    /// <summary>
    /// Starts Explorer with <paramref name="arguments"/> and waits until it exits.
    /// </summary>
    /// <param name="arguments">The arguments of Explorer.</param>
    /// <param name="cancellationToken">Cancels waiting for Explorer.</param>
    /// <returns>A task that completes when Explorer has exited.</returns>
    private async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        await processRunner.RunAsync(
            new()
            {
                FileName = Path.Combine(_windowsFolder, "explorer.exe"),
                Arguments = arguments,
                WorkingDirectory = _windowsFolder,
            },
            cancellationToken);
}
