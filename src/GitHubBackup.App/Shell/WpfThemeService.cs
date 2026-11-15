using System.Windows;
using GitHubBackup.Core.Ui;

namespace GitHubBackup.App.Shell;

/// <summary>
/// Implements <see cref="IThemeService"/> with the Fluent theme of WPF, set on the application so that every window
/// follows it.
/// </summary>
/// <remarks>Without a running application there is no window to style, so the call does nothing.</remarks>
internal sealed class WpfThemeService : IThemeService
{
    /// <inheritdoc />
    public void Apply(UiTheme theme)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        // The theme mode API of WPF is marked experimental; it is the only way to switch the Fluent theme at run time.
#pragma warning disable WPF0001
        application.ThemeMode = theme switch
        {
            UiTheme.Light => ThemeMode.Light,
            UiTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
#pragma warning restore WPF0001
    }
}
