using System.ComponentModel;
using System.Windows;
using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Views;

/// <summary>
/// Hosts the main window of the application, which shows the view of the current ViewModel.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>The ViewModel of the window, which decides whether the window may close.</summary>
    private readonly MainWindowViewModel _viewModel;

    /// <summary>Whether the ViewModel has allowed the window to close, so the next close goes through.</summary>
    private bool _closeAllowed;

    /// <summary>
    /// Initializes the window and binds it to <paramref name="viewModel"/>.
    /// </summary>
    /// <param name="viewModel">The ViewModel of the window.</param>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closing += OnClosing;
    }

    /// <summary>
    /// Asks the ViewModel whether the window may close and closes it again once it may.
    /// </summary>
    /// <remarks>
    /// WPF does not allow <see cref="Window.Close"/> while the window is closing, so an answer that is ready at once
    /// sets <see cref="CancelEventArgs.Cancel"/> directly; a later answer closes the window again.
    /// </remarks>
    /// <param name="sender">The window.</param>
    /// <param name="e">Lets the handler keep the window open.</param>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAllowed)
        {
            return;
        }

        var canClose = _viewModel.CanCloseAsync();
        if (canClose.IsCompleted)
        {
            e.Cancel = !await canClose;
            return;
        }

        e.Cancel = true;
        if (await canClose)
        {
            _closeAllowed = true;
            Close();
        }
    }
}
