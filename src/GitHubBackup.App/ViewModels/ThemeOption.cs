using GitHubBackup.Core.Ui;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One entry of the theme list of the settings editor.
/// </summary>
/// <param name="Value">The colour mode.</param>
/// <param name="Label">The text the list shows.</param>
public sealed record ThemeOption(UiTheme Value, string Label)
{
    /// <summary>Gets the entries of the list: the system mode first, as the default, then light and dark.</summary>
    public static IReadOnlyList<ThemeOption> All { get; } =
    [
        new(UiTheme.System, "System"),
        new(UiTheme.Light, "Light"),
        new(UiTheme.Dark, "Dark"),
    ];

    /// <inheritdoc />
    public override string ToString() => Label;
}
