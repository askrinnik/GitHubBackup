namespace GitHubBackup.App.Dialogs;

/// <summary>
/// Shows modal messages to the user, so that ViewModels and the start-up code stay testable without a window.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows an error and waits until the user closes it.
    /// </summary>
    /// <param name="title">The title of the dialog.</param>
    /// <param name="message">The text of the error.</param>
    void ShowError(string title, string message);

    /// <summary>
    /// Shows information and waits until the user closes it.
    /// </summary>
    /// <param name="title">The title of the dialog.</param>
    /// <param name="message">The text of the information.</param>
    void ShowInformation(string title, string message);

    /// <summary>
    /// Asks the user to confirm an action.
    /// </summary>
    /// <param name="title">The title of the dialog.</param>
    /// <param name="message">The question.</param>
    /// <returns><see langword="true"/> when the user confirms.</returns>
    bool Confirm(string title, string message);
}
