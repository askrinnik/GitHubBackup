using System.Text.RegularExpressions;

namespace GitHubBackup.Core.Security;

/// <summary>
/// Masks registered secrets, <c>Authorization</c> header values and GitHub tokens; safe to use from several threads.
/// </summary>
public sealed partial class SecretMasker : ISecretMasker
{
    /// <summary>The text that replaces a secret.</summary>
    public const string Replacement = "***";

    private readonly Lock _registrationLock = new();

    /// <summary>
    /// The registered secrets, longest first, so that a secret containing a shorter one is replaced as a whole.
    /// The array is replaced, never changed, so <see cref="Mask"/> reads it without the lock.
    /// </summary>
    private string[] _secrets = [];

    /// <inheritdoc />
    public void Register(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        lock (_registrationLock)
        {
            if (!_secrets.Contains(secret, StringComparer.Ordinal))
            {
                _secrets = [.. _secrets.Append(secret).OrderByDescending(value => value.Length)];
            }
        }
    }

    /// <inheritdoc />
    public string Mask(string text)
    {
        var masked = text;
        foreach (var secret in Volatile.Read(ref _secrets))
        {
            masked = masked.Replace(secret, Replacement, StringComparison.Ordinal);
        }

        masked = AuthorizationHeader().Replace(masked, "${scheme}" + Replacement);
        return GitHubToken().Replace(masked, Replacement);
    }

    /// <summary>
    /// Matches an <c>Authorization</c> header with the <c>Bearer</c>, <c>Basic</c> or <c>token</c> scheme; the
    /// <c>scheme</c> group keeps the header name and scheme.
    /// </summary>
    /// <remarks>
    /// Quotes and backslashes are allowed around the separator so that a header serialized as a JSON property is
    /// found too. The value is limited to the characters of a token or Base64 text, so a closing quote of a JSON
    /// string stays in place and the JSON output remains valid.
    /// </remarks>
    /// <returns>The regular expression.</returns>
    [GeneratedRegex(
        """(?<scheme>Authorization[\\"']*\s*[:=]\s*[\\"']*(?:Bearer|Basic|token)\s+)[A-Za-z0-9\-._~+/]+=*""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeader();

    /// <summary>
    /// Matches a GitHub token by its documented prefix: classic and OAuth tokens (<c>ghp_</c>, <c>gho_</c>,
    /// <c>ghu_</c>, <c>ghs_</c>, <c>ghr_</c>) and fine-grained tokens (<c>github_pat_</c>).
    /// </summary>
    /// <remarks>
    /// A token must not follow a word character, so identifiers that merely contain a prefix are left alone. The
    /// exception is a token that directly follows a JSON escape (<c>\n</c>, <c>\t</c>, <c>\uXXXX</c>) or a percent
    /// escape (<c>%3A</c>): the escape ends in a word character but is not part of the token's word. A preceding
    /// <c>\\n</c> is treated as an escape as well, which masks too much rather than too little.
    /// </remarks>
    /// <returns>The regular expression.</returns>
    [GeneratedRegex(
        """(?:(?<![A-Za-z0-9_])|(?<=\\[nrtbf]|\\u[0-9A-Fa-f]{4}|%[0-9A-Fa-f]{2}))(?:gh[pousr]_[A-Za-z0-9_]{36,}|github_pat_[A-Za-z0-9_]{22,})""",
        RegexOptions.CultureInvariant)]
    private static partial Regex GitHubToken();
}
