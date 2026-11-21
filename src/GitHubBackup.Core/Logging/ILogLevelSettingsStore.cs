namespace GitHubBackup.Core.Logging;

/// <summary>
/// Reads the effective level of each <see cref="LogEndpoint"/> and writes the levels the user sets in the settings
/// editor.
/// </summary>
/// <remarks>
/// The written levels take effect the next time the application starts. The source-context overrides of the endpoints
/// are not part of the settings: a save keeps them as they are.
/// </remarks>
public interface ILogLevelSettingsStore
{
    /// <summary>
    /// Gets the levels the last save of this run wrote, or the levels the running application uses before the first
    /// save, so that the settings editor shows what the next start uses.
    /// </summary>
    LogLevelSettings Current { get; }

    /// <summary>
    /// Gets the default level of the running application, which an endpoint without a level of its own takes.
    /// </summary>
    EndpointLogLevel DefaultLevel { get; }

    /// <summary>
    /// Writes the level of each endpoint to the application settings file atomically, keeping every other key and
    /// the previous file as a backup.
    /// </summary>
    /// <param name="settings">The levels to write.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the levels are written.</returns>
    /// <exception cref="IOException">
    /// The file cannot be read or written, or holds text that is not a JSON object; an existing file is left as it is.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read or written; an existing file is left as it is.</exception>
    Task SaveAsync(LogLevelSettings settings, CancellationToken cancellationToken);
}
