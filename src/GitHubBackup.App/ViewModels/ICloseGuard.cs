namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Lets a ViewModel decide whether the main window may close, and finish its work first.
/// </summary>
public interface ICloseGuard
{
    /// <summary>
    /// Returns whether the window may close, after any work that must not be abandoned has finished.
    /// </summary>
    /// <returns>A task that returns <see langword="true"/> when the window may close.</returns>
    Task<bool> CanCloseAsync();
}
