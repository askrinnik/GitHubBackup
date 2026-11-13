using System.Windows;

namespace GitHubBackup.App.Dialogs;

/// <summary>
/// Shows the dialogs of <see cref="IDialogService"/> as WPF message boxes.
/// </summary>
/// <remarks>
/// A message box is owned by the main window while that window is visible, so it stays in front of it; before the
/// main window is shown, for example when the host fails to start, the message box has no owner.
/// </remarks>
internal sealed class MessageBoxDialogService : IDialogService
{
    /// <inheritdoc />
    public void ShowError(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <inheritdoc />
    public void ShowInformation(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <inheritdoc />
    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>
    /// Shows a message box, owned by the main window when it is visible.
    /// </summary>
    /// <param name="message">The text of the message box.</param>
    /// <param name="title">The title of the message box.</param>
    /// <param name="buttons">The buttons of the message box.</param>
    /// <param name="image">The icon of the message box.</param>
    /// <returns>The button the user chose.</returns>
    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Application.Current?.MainWindow is { IsVisible: true } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
