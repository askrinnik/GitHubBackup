using System.Windows;
using System.Windows.Controls;
using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Views;

/// <summary>
/// Shows the settings editor: the backup configuration, the tool paths and the GitHub token.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    /// <summary>
    /// Initializes the view and loads its XAML-defined content.
    /// </summary>
    public SettingsView() => InitializeComponent();

    /// <summary>
    /// Reads the configuration when the view first appears; the ViewModel reads nothing while the container creates it.
    /// </summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">No data.</param>
    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.LoadCommand.Execute(null);
}
