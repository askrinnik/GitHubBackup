using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Fails the start of a host by throwing a given exception from <see cref="StartAsync"/>, or the cancellation of the
/// start token when no exception is given.
/// </summary>
/// <param name="exception">The exception to throw, or <see langword="null"/> to throw only on cancellation.</param>
internal sealed class ThrowingHostedService(Exception? exception) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return exception is null ? Task.CompletedTask : throw exception;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
