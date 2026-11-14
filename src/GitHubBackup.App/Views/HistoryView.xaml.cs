using System.Windows;
using System.Windows.Controls;
using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Views;

/// <summary>
/// Shows the history screen: the runs with their details and the archive history of a repository.
/// </summary>
public sealed partial class HistoryView : UserControl
{
    /// <summary>
    /// Initializes the view and loads its XAML-defined content.
    /// </summary>
    public HistoryView() => InitializeComponent();

    /// <summary>
    /// Reads the runs and the repositories when the view first appears; the ViewModel reads nothing while the container
    /// creates it.
    /// </summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">No data.</param>
    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as HistoryViewModel)?.LoadCommand.Execute(null);
}
