using System.Windows;
using GitHubBackup.App.Runs;

namespace GitHubBackup.App.Hosting;

/// <summary>
/// Implements <see cref="IUiDispatcher"/> with the dispatcher of the running WPF application.
/// </summary>
/// <remarks>
/// The dispatcher is looked up on each call rather than captured when the service is created, because the container
/// may create the service on any thread. Without a running application there is no window to update, so the work is
/// dropped; a dispatcher that is shutting down drops it too.
/// </remarks>
internal sealed class WpfUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}
