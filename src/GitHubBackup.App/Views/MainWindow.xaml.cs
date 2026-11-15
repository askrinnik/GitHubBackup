using System.ComponentModel;
using System.Windows;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Ui;

namespace GitHubBackup.App.Views;

/// <summary>
/// Hosts the main window of the application, which shows the view of the current ViewModel and opens where it was
/// closed last time.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>The ViewModel of the window, which decides whether the window may close.</summary>
    private readonly MainWindowViewModel _viewModel;

    /// <summary>Whether the ViewModel has allowed the window to close, so the next close goes through.</summary>
    private bool _closeAllowed;

    /// <summary>
    /// Initializes the window, binds it to <paramref name="viewModel"/> and applies the placement it opens with.
    /// </summary>
    /// <param name="viewModel">The ViewModel of the window.</param>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        ApplyStartupPlacement();
        Closing += OnClosing;
    }

    /// <summary>
    /// Applies the placement the ViewModel chooses; it must run before the window is shown, so the window never
    /// appears at the default place first.
    /// </summary>
    /// <remarks>
    /// A window maximized before it is shown maximizes on the monitor of its normal bounds, which become its
    /// <see cref="Window.RestoreBounds"/>; without bounds it opens centred on the primary monitor.
    /// </remarks>
    private void ApplyStartupPlacement()
    {
        var placement = _viewModel.GetStartupPlacement();
        if (placement.Bounds is { } bounds)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Hands the normal bounds of the window and whether it is maximized to the ViewModel; a minimized window
    /// reports its normal bounds and opens in the normal state next time.
    /// </summary>
    private void RecordPlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _viewModel.RecordPlacement(new WindowPlacement(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized));
    }

    /// <summary>
    /// Asks the ViewModel whether the window may close and closes it again once it may; the placement is recorded
    /// only when the window really closes.
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
            RecordPlacement();
            return;
        }

        var canClose = _viewModel.CanCloseAsync();
        if (canClose.IsCompleted)
        {
            e.Cancel = !await canClose;
            if (!e.Cancel)
            {
                RecordPlacement();
            }

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
