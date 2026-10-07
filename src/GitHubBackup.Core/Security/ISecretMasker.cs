namespace GitHubBackup.Core.Security;

/// <summary>
/// Replaces secrets in text that leaves the process: log output, logged command lines and environment values.
/// </summary>
/// <remarks>
/// Besides the registered values, the masker recognises the value of an <c>Authorization</c> header and GitHub tokens
/// by their format, so a secret that reaches the text before it is registered is masked too.
/// </remarks>
public interface ISecretMasker
{
    /// <summary>
    /// Adds <paramref name="secret"/> to the values that <see cref="Mask"/> replaces.
    /// </summary>
    /// <param name="secret">The secret value, for example a token; registering it again has no effect.</param>
    /// <exception cref="ArgumentException"><paramref name="secret"/> is empty or white space.</exception>
    void Register(string secret);

    /// <summary>
    /// Returns <paramref name="text"/> with every secret replaced by <c>***</c>.
    /// </summary>
    /// <param name="text">The text to mask.</param>
    /// <returns>The masked text.</returns>
    string Mask(string text);
}
