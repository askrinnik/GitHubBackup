using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Dialogs;

/// <summary>
/// Shows the discovery dialog, so the decision handler of a run stays testable without a window.
/// </summary>
public interface IDiscoveryDialogService
{
    /// <summary>
    /// Shows the dialog modally over the main window and waits until it closes.
    /// </summary>
    /// <remarks>The method may be called from any thread; the dialog itself runs on the UI thread.</remarks>
    /// <param name="viewModel">The ViewModel of the dialog.</param>
    /// <param name="cancellationToken">Closes the dialog as if the user cancelled.</param>
    /// <returns>
    /// <see langword="true"/> when the user applied the decisions; <see langword="false"/> when the user cancelled,
    /// closed the dialog or <paramref name="cancellationToken"/> closed it.
    /// </returns>
    Task<bool> ShowAsync(DiscoveryReviewViewModel viewModel, CancellationToken cancellationToken);
}
