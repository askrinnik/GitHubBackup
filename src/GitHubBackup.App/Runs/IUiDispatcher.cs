namespace GitHubBackup.App.Runs;

/// <summary>
/// Runs work on the UI thread, so code on a background thread can update what a window binds to without a reference
/// to WPF.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Queues <paramref name="action"/> to run on the UI thread and returns without waiting for it.
    /// </summary>
    /// <param name="action">The work to run.</param>
    void Post(Action action);
}
