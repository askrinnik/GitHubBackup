using Microsoft.Extensions.Hosting;

namespace GitHubBackup.App.Hosting;

/// <summary>
/// Lets WPF own the lifetime of the process: the host neither waits for Ctrl+C nor writes console messages, and it
/// stops when the application stops it after the main window closes.
/// </summary>
internal sealed class DesktopHostLifetime : IHostLifetime
{
    /// <inheritdoc />
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
