using CommunityToolkit.Mvvm.ComponentModel;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// An entry of the navigation of the settings editor: the section it opens, whether it is open and whether a field of
/// the section shows an error.
/// </summary>
/// <param name="section">The section.</param>
/// <param name="title">The title shown in the navigation.</param>
/// <param name="glyph">The icon of the section, a character of the Segoe Fluent Icons font.</param>
public sealed partial class SettingsSectionItem(SettingsSection section, string title, string glyph) : ObservableObject
{
    /// <summary>Whether the section is the one the editor shows.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether a field of the section shows an error.</summary>
    [ObservableProperty]
    private bool _hasErrors;

    /// <summary>Gets the section.</summary>
    public SettingsSection Section { get; } = section;

    /// <summary>Gets the title shown in the navigation.</summary>
    public string Title { get; } = title;

    /// <summary>Gets the icon shown before the title, a character of the Segoe Fluent Icons font.</summary>
    public string Glyph { get; } = glyph;

    /// <summary>Gets the UI Automation id of the navigation entry, such as <c>SettingsSectionGeneral</c>.</summary>
    public string AutomationId => $"SettingsSection{Section}";
}
