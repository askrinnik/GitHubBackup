namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Finds the field of the settings editor that a validation message of <c>backup-config.json</c> belongs to.
/// </summary>
/// <remarks>
/// Every message of the validator starts with its location in the file, such as <c>accounts[1].url</c>. The longest
/// known location that starts the message wins, provided the message continues with a space, a <c>.</c> or a
/// <c>[</c> after it; so a message about a property the editor does not show, such as
/// <c>accounts[0].repositories[2].id</c>, falls back to the location of its entry.
/// </remarks>
internal static class SettingsErrorMapper
{
    /// <summary>
    /// Returns the known location that <paramref name="message"/> belongs to.
    /// </summary>
    /// <param name="message">The validation message.</param>
    /// <param name="locations">The locations the editor shows a field for.</param>
    /// <returns>The longest matching location, or <see langword="null"/> when none matches.</returns>
    public static string? FindLocation(string message, IEnumerable<string> locations)
    {
        string? best = null;
        foreach (var location in locations)
        {
            if (location.Length > (best?.Length ?? 0) && IsLocationOf(message, location))
            {
                best = location;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns whether <paramref name="message"/> starts with <paramref name="location"/> as a whole location.
    /// </summary>
    /// <param name="message">The validation message.</param>
    /// <param name="location">The location.</param>
    /// <returns><see langword="true"/> when the location is followed by the end, a space, a <c>.</c> or a <c>[</c>.</returns>
    private static bool IsLocationOf(string message, string location) =>
        message.StartsWith(location, StringComparison.Ordinal)
        && (message.Length == location.Length || message[location.Length] is ' ' or '.' or '[');
}
