using System.Windows;
using GitHubBackup.App.ViewModels;
using GitHubBackup.App.Views;

namespace GitHubBackup.App.Dialogs;

/// <summary>
/// Shows the discovery dialog as a modal <see cref="DiscoveryReviewWindow"/> on the UI thread.
/// </summary>
/// <remarks>
/// The run calls the service from the thread pool, so the window is created through the dispatcher of the running
/// application. The dialog is owned by the main window while that window is visible, so it stays in front of it. A
/// cancelled token closes the window on the UI thread, which counts as a cancel.
/// </remarks>
internal sealed class WpfDiscoveryDialogService : IDiscoveryDialogService
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No WPF application is running.</exception>
    public async Task<bool> ShowAsync(DiscoveryReviewViewModel viewModel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("The discovery dialog needs a running WPF application.");
        return await dispatcher.InvokeAsync(() => ShowDialog(viewModel, cancellationToken));
    }

    /// <summary>
    /// Shows the dialog and waits until it closes; runs on the UI thread.
    /// </summary>
    /// <param name="viewModel">The ViewModel of the dialog.</param>
    /// <param name="cancellationToken">Closes the dialog.</param>
    /// <returns><see langword="true"/> when the user applied the decisions.</returns>
    private static bool ShowDialog(DiscoveryReviewViewModel viewModel, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var window = new DiscoveryReviewWindow(viewModel);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        using var registration = cancellationToken.Register(() => window.Dispatcher.InvokeAsync(window.Close));
        return window.ShowDialog() == true;
    }
}
