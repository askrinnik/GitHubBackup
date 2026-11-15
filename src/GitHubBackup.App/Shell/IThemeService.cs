using GitHubBackup.Core.Ui;

namespace GitHubBackup.App.Shell;

/// <summary>
/// Switches the colour mode of every window of the application, so that ViewModels choose a theme without knowing WPF.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Applies <paramref name="theme"/> to the open windows and to every window opened later.
    /// </summary>
    /// <param name="theme">The colour mode.</param>
    void Apply(UiTheme theme);
}
