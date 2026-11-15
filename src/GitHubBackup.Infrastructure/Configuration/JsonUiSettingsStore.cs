using System.Text.Json;
using GitHubBackup.Core.Ui;
using GitHubBackup.Infrastructure.Hosting;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads and writes the user interface preferences in <see cref="GitHubBackupHostBuilder.UiSettingsFileName"/>.
/// </summary>
/// <remarks>
/// Every value is read on its own, so a value of the wrong type or out of range spoils only itself and falls back to
/// its default.
/// </remarks>
/// <param name="file">The user interface settings file.</param>
internal sealed class JsonUiSettingsStore(UiSettingsFile file) : IUiSettingsStore
{
    /// <inheritdoc />
    public async Task<UiSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (await file.ReadAsync(cancellationToken) is not { } root)
        {
            return UiSettings.Default;
        }

        var theme = ReadTheme(root);
        if (JsonSettingsText.TryGetProperty(root, UiSettingsFile.MainWindowKey) is not { ValueKind: JsonValueKind.Object } window)
        {
            return UiSettings.Default with { Theme = theme };
        }

        var placement = TryGetNumber(window, nameof(WindowPlacement.Left)) is { } left
            && TryGetNumber(window, nameof(WindowPlacement.Top)) is { } top
            && TryGetNumber(window, nameof(WindowPlacement.Width)) is { } width
            && TryGetNumber(window, nameof(WindowPlacement.Height)) is { } height
                ? new WindowPlacement(
                    left,
                    top,
                    width,
                    height,
                    JsonSettingsText.TryGetProperty(window, nameof(WindowPlacement.IsMaximized))?.ValueKind == JsonValueKind.True)
                : null;

        return new UiSettings(
            theme,
            placement,
            TryGetNumber(window, nameof(UiSettings.LogPanelHeight)) ?? UiSettings.DefaultLogPanelHeight,
            TryGetNumber(window, nameof(UiSettings.SummaryPanelHeight)) ?? UiSettings.DefaultSummaryPanelHeight)
            .Sanitize();
    }

    /// <inheritdoc />
    public Task SaveAsync(UiSettings settings, CancellationToken cancellationToken)
    {
        var sanitized = settings.Sanitize();
        return file.WriteAsync(
            json =>
            {
                json.WriteStartObject();
                json.WriteString(UiSettingsFile.ThemeKey, sanitized.Theme.ToString());
                json.WriteStartObject(UiSettingsFile.MainWindowKey);
                if (sanitized.MainWindow is { } placement)
                {
                    json.WriteNumber(nameof(WindowPlacement.Left), placement.Left);
                    json.WriteNumber(nameof(WindowPlacement.Top), placement.Top);
                    json.WriteNumber(nameof(WindowPlacement.Width), placement.Width);
                    json.WriteNumber(nameof(WindowPlacement.Height), placement.Height);
                    json.WriteBoolean(nameof(WindowPlacement.IsMaximized), placement.IsMaximized);
                }

                json.WriteNumber(nameof(UiSettings.LogPanelHeight), sanitized.LogPanelHeight);
                json.WriteNumber(nameof(UiSettings.SummaryPanelHeight), sanitized.SummaryPanelHeight);
                json.WriteEndObject();
                json.WriteEndObject();
            },
            cancellationToken);
    }

    /// <summary>
    /// Reads the theme: one of the names of <see cref="UiTheme"/> without regard to case, otherwise
    /// <see cref="UiTheme.System"/>.
    /// </summary>
    /// <param name="root">The root object of the file.</param>
    /// <returns>The theme.</returns>
    private static UiTheme ReadTheme(JsonElement root)
    {
        if (JsonSettingsText.TryGetProperty(root, UiSettingsFile.ThemeKey) is not { ValueKind: JsonValueKind.String } value)
        {
            return UiTheme.System;
        }

        var text = value.GetString();
        return Enum.GetValues<UiTheme>()
            .Where(theme => string.Equals(theme.ToString(), text, StringComparison.OrdinalIgnoreCase))
            .DefaultIfEmpty(UiTheme.System)
            .First();
    }

    /// <summary>
    /// Returns the number of a property, or <see langword="null"/> when the property is missing or not a number.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name, compared without regard to case.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    private static double? TryGetNumber(JsonElement element, string name) =>
        JsonSettingsText.TryGetProperty(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var number)
            ? number
            : null;
}
