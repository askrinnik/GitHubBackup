using System.Text.Json;
using GitHubBackup.Core.Ui;
using GitHubBackup.Infrastructure.Hosting;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads and writes the <c>Ui</c> section of <see cref="GitHubBackupHostBuilder.UserSettingsFileName"/>.
/// </summary>
/// <remarks>
/// The section is read from the file itself, not from the host configuration, which leaves it out: the console
/// application never sees it, and an invalid value falls back to its default instead of failing the start. Every
/// value is read on its own, so a value of the wrong type spoils only itself.
/// </remarks>
/// <param name="file">The user settings file.</param>
internal sealed class JsonUiSettingsStore(UserSettingsFile file) : IUiSettingsStore
{
    /// <inheritdoc />
    public async Task<UiSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var section = await file.ReadSectionAsync(UserSettingsFile.UiSectionName, cancellationToken);
        if (section is not { ValueKind: JsonValueKind.Object } ui)
        {
            return UiSettings.Default;
        }

        var theme = ReadTheme(ui);
        if (TryGetProperty(ui, UserSettingsFile.UiMainWindowKey) is not { ValueKind: JsonValueKind.Object } window)
        {
            return UiSettings.Default with { Theme = theme };
        }

        var placement = TryGetNumber(window, nameof(WindowPlacement.Left)) is { } left
            && TryGetNumber(window, nameof(WindowPlacement.Top)) is { } top
            && TryGetNumber(window, nameof(WindowPlacement.Width)) is { } width
            && TryGetNumber(window, nameof(WindowPlacement.Height)) is { } height
                ? new WindowPlacement(
                    left, top, width, height, TryGetProperty(window, nameof(WindowPlacement.IsMaximized))?.ValueKind == JsonValueKind.True)
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
        return file.WriteSectionAsync(
            UserSettingsFile.UiSectionName,
            json =>
            {
                json.WriteStartObject();
                json.WriteString(UserSettingsFile.UiThemeKey, sanitized.Theme.ToString());
                json.WriteStartObject(UserSettingsFile.UiMainWindowKey);
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
    /// <param name="ui">The <c>Ui</c> object.</param>
    /// <returns>The theme.</returns>
    private static UiTheme ReadTheme(JsonElement ui)
    {
        if (TryGetProperty(ui, UserSettingsFile.UiThemeKey) is not { ValueKind: JsonValueKind.String } value)
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
        TryGetProperty(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var number)
            ? number
            : null;

    /// <summary>
    /// Returns a property compared without regard to case, as the configuration compares keys; of two properties
    /// with the same name the later one wins.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value of the property, or <see langword="null"/> when there is none.</returns>
    private static JsonElement? TryGetProperty(JsonElement element, string name) =>
        element.EnumerateObject()
            .Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(property => (JsonElement?)property.Value)
            .LastOrDefault();
}
