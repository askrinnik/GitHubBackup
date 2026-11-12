namespace GitHubBackup.Core.Authentication;

/// <summary>
/// Reports that an <see cref="ITokenStore"/> cannot be read or written.
/// </summary>
/// <param name="message">The message, naming the store and the reason; it never contains the token.</param>
/// <param name="innerException">The exception that caused the failure, or <see langword="null"/>.</param>
public sealed class TokenStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
