namespace GitHubBackup.Core.Telemetry;

/// <summary>
/// Reads the effective telemetry settings and writes the settings the user sets in the settings editor.
/// </summary>
/// <remarks>The written settings take effect the next time the application starts.</remarks>
public interface ITelemetrySettingsStore
{
    /// <summary>
    /// Gets the telemetry settings the last save of this run wrote, or the settings the running application uses before
    /// the first save, so that the settings editor shows what the next start uses.
    /// </summary>
    /// <remarks>
    /// <see cref="TelemetrySettings.Headers"/> is the value of the application settings file only: the last save of
    /// this run, or the file as read at start-up. A value from a source that overrides the file is a secret the user
    /// did not put there, so it is never shown and never carried into the file.
    /// </remarks>
    TelemetrySettings Current { get; }

    /// <summary>
    /// Gets whether a configuration source with a higher priority than the application settings file sets
    /// <see cref="TelemetrySettings.HeadersKey"/>, so that the headers of the file do not take effect.
    /// </summary>
    bool HeadersOverridden { get; }

    /// <summary>
    /// Checks <paramref name="settings"/> by the rules applied to the configured telemetry settings at start-up.
    /// </summary>
    /// <param name="settings">The settings to check.</param>
    /// <returns>
    /// The errors, empty when the settings are valid; each starts with <see cref="TelemetrySettings.EnabledKey"/>,
    /// <see cref="TelemetrySettings.OtlpEndpointKey"/> or <see cref="TelemetrySettings.HeadersKey"/> and never quotes
    /// the value.
    /// </returns>
    IReadOnlyList<string> Validate(TelemetrySettings settings);

    /// <summary>
    /// Validates <paramref name="settings"/> and writes them atomically, keeping the previous file as a backup.
    /// </summary>
    /// <param name="settings">The settings to write.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the settings are written.</returns>
    /// <exception cref="ArgumentException"><paramref name="settings"/> fails validation; nothing is written.</exception>
    /// <exception cref="IOException">
    /// The file cannot be read or written, or holds text that is not a JSON object; an existing file is left as it is.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read or written; an existing file is left as it is.</exception>
    Task SaveAsync(TelemetrySettings settings, CancellationToken cancellationToken);
}
