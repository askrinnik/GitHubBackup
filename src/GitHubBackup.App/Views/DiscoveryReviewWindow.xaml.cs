using System.Windows;
using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Views;

/// <summary>
/// Hosts the discovery dialog: the table of discovered repositories and the buttons that apply or cancel the decisions.
/// </summary>
public sealed partial class DiscoveryReviewWindow : Window
{
    /// <summary>
    /// Initializes the window and binds it to <paramref name="viewModel"/>.
    /// </summary>
    /// <param name="viewModel">The ViewModel of the dialog.</param>
    public DiscoveryReviewWindow(DiscoveryReviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
    }

    /// <summary>
    /// Closes the dialog with the result the ViewModel asks for.
    /// </summary>
    /// <param name="sender">The ViewModel.</param>
    /// <param name="applied">Whether the decisions are applied.</param>
    private void OnCloseRequested(object? sender, bool applied) => DialogResult = applied;
}
