using System.Windows;
using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Views;

/// <summary>
/// Hosts the main window of the application, which shows the view of the current ViewModel.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Initializes the window and binds it to <paramref name="viewModel"/>.
    /// </summary>
    /// <param name="viewModel">The ViewModel of the window.</param>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
