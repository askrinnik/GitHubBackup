namespace GitHubBackup.Core.Telemetry;

/// <summary>
/// Reports that an <see cref="IOtlpHeadersStore"/> cannot be read or written.
/// </summary>
/// <param name="message">The message, naming the store and the reason; it never contains the headers.</param>
/// <param name="innerException">The exception that caused the failure, or <see langword="null"/>.</param>
public sealed class OtlpHeadersStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
