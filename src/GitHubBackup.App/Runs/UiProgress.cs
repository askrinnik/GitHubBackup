namespace GitHubBackup.App.Runs;

/// <summary>
/// Implements <see cref="IProgress{T}"/> by posting each report to the UI thread through <see cref="IUiDispatcher"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="Progress{T}"/>, it does not capture the synchronization context of the thread that creates it, so
/// a test can run the posted work with a dispatcher of its own and observe each report deterministically.
/// </remarks>
/// <typeparam name="T">The type of the reported value.</typeparam>
/// <param name="dispatcher">Runs <paramref name="handler"/> on the UI thread.</param>
/// <param name="handler">Receives each reported value on the UI thread.</param>
public sealed class UiProgress<T>(IUiDispatcher dispatcher, Action<T> handler) : IProgress<T>
{
    /// <inheritdoc />
    public void Report(T value) => dispatcher.Post(() => handler(value));
}
