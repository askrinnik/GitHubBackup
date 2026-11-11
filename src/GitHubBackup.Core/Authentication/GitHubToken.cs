namespace GitHubBackup.Core.Authentication;

/// <summary>
/// The outcome of the token lookup: a token and the source it came from, or anonymous access.
/// </summary>
/// <remarks>
/// This is a class, not a record, so that no generated <see cref="ToString"/> prints the token.
/// </remarks>
public sealed class GitHubToken
{
    /// <summary>
    /// Initializes a token found in a source.
    /// </summary>
    /// <param name="value">The token value.</param>
    /// <param name="sourceName">The name of the source the token came from.</param>
    public GitHubToken(string value, string sourceName)
    {
        Value = value;
        SourceName = sourceName;
    }

    private GitHubToken()
    {
        SourceName = "anonymous";
    }

    /// <summary>
    /// Gets the shared instance that stands for access without a token.
    /// </summary>
    public static GitHubToken Anonymous { get; } = new();

    /// <summary>
    /// Gets the token value, or <see langword="null"/> in anonymous mode.
    /// </summary>
    public string? Value { get; }

    /// <summary>
    /// Gets the name of the source the token came from, or <c>anonymous</c>.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets a value that tells whether there is no token.
    /// </summary>
    public bool IsAnonymous => Value is null;

    /// <summary>
    /// Returns the source name; the token value is never part of it.
    /// </summary>
    /// <returns>The description of this token.</returns>
    public override string ToString() => $"GitHubToken(Source: {SourceName})";
}
