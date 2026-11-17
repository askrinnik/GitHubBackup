using System.Globalization;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Builds the text of a <c>ui-settings.json</c> with a theme, the size of the main window and the heights of its panels;
/// a value left <see langword="null"/> is not written, so the application uses its default.
/// </summary>
public sealed class UiSettingsJson
{
    /// <summary>Gets the theme name, <c>light</c>, <c>dark</c> or <c>system</c>.</summary>
    public string? Theme { get; init; }

    /// <summary>
    /// Gets the width and the height of the main window in device-independent pixels; the window opens at the top left
    /// corner of the primary monitor.
    /// </summary>
    public (double Width, double Height)? WindowSize { get; init; }

    /// <summary>Gets the height of the live log panel in device-independent pixels.</summary>
    public double? LogPanelHeight { get; init; }

    /// <summary>Gets the height of the results panel in device-independent pixels.</summary>
    public double? SummaryPanelHeight { get; init; }

    /// <summary>
    /// Returns the JSON text.
    /// </summary>
    /// <returns>The content of <c>ui-settings.json</c>.</returns>
    public string Build()
    {
        List<string> root = [];
        if (Theme is not null)
        {
            root.Add($"\"Theme\": \"{Theme}\"");
        }

        List<string> mainWindow = [];
        if (WindowSize is { } size)
        {
            mainWindow.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"\"Left\": 0, \"Top\": 0, \"Width\": {size.Width}, \"Height\": {size.Height}, \"IsMaximized\": false"));
        }

        if (LogPanelHeight is { } log)
        {
            mainWindow.Add(string.Create(CultureInfo.InvariantCulture, $"\"LogPanelHeight\": {log}"));
        }

        if (SummaryPanelHeight is { } summary)
        {
            mainWindow.Add(string.Create(CultureInfo.InvariantCulture, $"\"SummaryPanelHeight\": {summary}"));
        }

        if (mainWindow.Count > 0)
        {
            root.Add($"\"MainWindow\": {{ {string.Join(", ", mainWindow)} }}");
        }

        return $"{{ {string.Join(", ", root)} }}";
    }

    /// <summary>
    /// Writes the JSON text to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    public void WriteTo(string path) => File.WriteAllText(path, Build());
}
