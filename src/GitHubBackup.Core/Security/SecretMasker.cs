using System.Globalization;
using System.Text;
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
    /// The registered secrets in their raw and JSON-escaped forms, longest first, so that a secret containing a
    /// shorter one is replaced as a whole. The array is replaced, never changed, so <see cref="Mask"/> reads it
    /// without the lock.
    /// </summary>
    private string[] _secrets = [];

    /// <summary>
    /// Registers the raw secret and, when it differs, its JSON-escaped form, because a JSON log file stores the
    /// escaped text.
    /// </summary>
    /// <param name="secret">The secret value.</param>
    /// <inheritdoc cref="ISecretMasker.Register" path="/exception"/>
    public void Register(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var escaped = EscapeAsJsonString(secret);

        lock (_registrationLock)
        {
            var secrets = _secrets.ToList();
            AddIfMissing(secrets, secret);
            AddIfMissing(secrets, escaped);

            if (secrets.Count != _secrets.Length)
            {
                _secrets = [.. secrets.OrderByDescending(value => value.Length)];
            }
        }
    }

    /// <summary>
    /// Adds <paramref name="value"/> to <paramref name="secrets"/> unless the list already holds it.
    /// </summary>
    /// <param name="secrets">The list to extend.</param>
    /// <param name="value">The form of a secret.</param>
    private static void AddIfMissing(List<string> secrets, string value)
    {
        if (!secrets.Contains(value, StringComparer.Ordinal))
        {
            secrets.Add(value);
        }
    }

    /// <summary>
    /// Escapes <paramref name="value"/> the way the Serilog JSON formatter writes a string: quotes, backslashes and
    /// control characters are escaped, everything else, including non-ASCII text, is written as is.
    /// </summary>
    /// <param name="value">The raw text.</param>
    /// <returns>The text as it appears between the quotes of a JSON string.</returns>
    private static string EscapeAsJsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case < ' ':
                    builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
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
