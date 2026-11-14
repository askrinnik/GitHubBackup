using GitHubBackup.Core.Processes;

namespace GitHubBackup.App.Shell;

/// <summary>
/// Opens folders and files in Windows Explorer, so that ViewModels stay testable without starting a process.
/// </summary>
public interface IExplorerLauncher
{
    /// <summary>
    /// Opens a folder in a new Explorer window.
    /// </summary>
    /// <param name="folder">The absolute path of the folder.</param>
    /// <param name="cancellationToken">Cancels waiting for Explorer.</param>
    /// <returns>A task that completes when Explorer has taken the request.</returns>
    /// <exception cref="ProcessStartException">Explorer cannot be started.</exception>
    Task OpenFolderAsync(string folder, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the folder of a file in a new Explorer window with the file selected.
    /// </summary>
    /// <param name="filePath">The absolute path of the file.</param>
    /// <param name="cancellationToken">Cancels waiting for Explorer.</param>
    /// <returns>A task that completes when Explorer has taken the request.</returns>
    /// <exception cref="ProcessStartException">Explorer cannot be started.</exception>
    Task ShowFileAsync(string filePath, CancellationToken cancellationToken);
}
