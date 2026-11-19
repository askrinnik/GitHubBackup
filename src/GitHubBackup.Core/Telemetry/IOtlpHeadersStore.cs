namespace GitHubBackup.Core.Telemetry;

/// <summary>
/// The place where the application keeps the OTLP headers (<see cref="TelemetrySettings.HeadersKey"/>) the user saved.
/// </summary>
public interface IOtlpHeadersStore
{
    /// <summary>
    /// Gets a description of where the headers are kept, for messages to the user; it never contains the headers.
    /// </summary>
    string Location { get; }

    /// <summary>
    /// Reads the saved headers.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The headers, or <see langword="null"/> when none are saved.</returns>
    /// <exception cref="OtlpHeadersStoreException">The store cannot be read.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves <paramref name="headers"/>, replacing the headers saved earlier.
    /// </summary>
    /// <param name="headers">The header list to save.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>A task that completes when the headers are saved.</returns>
    /// <exception cref="OtlpHeadersStoreException">The store cannot be written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task WriteAsync(string headers, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the saved headers; does nothing when none are saved.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the removal.</param>
    /// <returns>A task that completes when no headers are saved.</returns>
    /// <exception cref="OtlpHeadersStoreException">The store cannot be written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task DeleteAsync(CancellationToken cancellationToken);
}
