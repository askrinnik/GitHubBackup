using System.Windows;
using System.Windows.Controls;
using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Views;

/// <summary>
/// Shows the tree of accounts and repositories and the commands that start a backup.
/// </summary>
public sealed partial class RepositoriesView : UserControl
{
    /// <summary>
    /// Initializes the view and loads its XAML-defined content.
    /// </summary>
    public RepositoriesView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Reads the tree when the view first appears; the ViewModel reads nothing while the container creates it.
    /// </summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">No data.</param>
    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as RepositoriesViewModel)?.RefreshCommand.Execute(null);
}
