namespace GitHubBackup.Core.Tools;

/// <summary>
/// Reads the effective tool paths and writes the paths the user sets in the settings editor.
/// </summary>
/// <remarks>The written paths take effect the next time the application starts.</remarks>
public interface IToolPathsStore
{
    /// <summary>Gets the tool paths the running application uses.</summary>
    ToolPaths Current { get; }

    /// <summary>
    /// Checks <paramref name="paths"/> by the rules applied to the configured tool paths at start-up.
    /// </summary>
    /// <param name="paths">The paths to check.</param>
    /// <returns>
    /// The errors, empty when the paths are valid; each starts with <see cref="ToolPaths.GitPathKey"/> or
    /// <see cref="ToolPaths.SevenZipPathKey"/> and never quotes the value.
    /// </returns>
    IReadOnlyList<string> Validate(ToolPaths paths);

    /// <summary>
    /// Validates <paramref name="paths"/> and writes them atomically, keeping the previous file as a backup.
    /// </summary>
    /// <param name="paths">The paths to write.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the paths are written.</returns>
    /// <exception cref="ArgumentException"><paramref name="paths"/> fails validation; nothing is written.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be written.</exception>
    Task SaveAsync(ToolPaths paths, CancellationToken cancellationToken);
}
