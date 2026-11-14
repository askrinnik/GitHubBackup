namespace GitHubBackup.Core.Ui;

/// <summary>
/// Reads and writes the preferences of the user interface.
/// </summary>
public interface IUiSettingsStore
{
    /// <summary>
    /// Reads the saved preferences.
    /// </summary>
    /// <remarks>
    /// A missing, empty or unreadable file and a missing or malformed section give <see cref="UiSettings.Default"/>;
    /// each invalid value is replaced by its default, so the result is always sanitized.
    /// </remarks>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The sanitized preferences.</returns>
    Task<UiSettings> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="settings"/> atomically, keeping the previous file as a backup and every other setting
    /// of the file as it is.
    /// </summary>
    /// <param name="settings">The preferences to write; invalid values are replaced by their defaults first.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the preferences are written.</returns>
    /// <exception cref="IOException">
    /// The file cannot be read or written, or holds text that is not a JSON object; an existing file is left as it is.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read or written; an existing file is left as it is.</exception>
    Task SaveAsync(UiSettings settings, CancellationToken cancellationToken);
}
