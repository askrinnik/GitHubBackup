namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Parses the value of <see cref="OpenTelemetryOptions.HeadersKey"/>: comma-separated <c>name=value</c> pairs.
/// </summary>
/// <remarks>
/// The value is split from the name at the first <c>=</c>, so a value may itself contain <c>=</c> (Base64 padding).
/// White space around names and values is dropped. The name is an HTTP header token; the value is not empty and holds
/// no control characters. Both rules hold before and after percent-decoding, because the OTLP exporter decodes names
/// and values before it sends them.
/// </remarks>
internal static class OtlpHeaders
{
    /// <summary>The characters an HTTP header token may hold besides letters and digits.</summary>
    private const string _tokenSymbols = "!#$%&'*+-.^_`|~";

    /// <summary>
    /// Parses <paramref name="headers"/> into name and value pairs.
    /// </summary>
    /// <param name="headers">The configured header list.</param>
    /// <param name="pairs">The pairs in their order when the list is well formed; otherwise empty.</param>
    /// <returns><see langword="true"/> when every segment is a well-formed pair; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string headers, out IReadOnlyList<KeyValuePair<string, string>> pairs)
    {
        var parsed = new List<KeyValuePair<string, string>>();
        pairs = [];
        foreach (var segment in headers.Split(','))
        {
            var separator = segment.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                return false;
            }

            var name = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (!IsToken(name) || !IsToken(Uri.UnescapeDataString(name)) || !IsHeaderValue(value)
                || !IsHeaderValue(Uri.UnescapeDataString(value)))
            {
                return false;
            }

            parsed.Add(new(name, value));
        }

        pairs = parsed;
        return true;
    }

    /// <summary>
    /// Returns, for each segment of <paramref name="headers"/> that has an <c>=</c>, the text after the first one,
    /// trimmed, when it is not empty.
    /// </summary>
    /// <remarks>Lets a malformed list still have its values masked, wherever the list is broken.</remarks>
    /// <param name="headers">The configured header list, well formed or not.</param>
    /// <returns>The candidate values.</returns>
    public static IEnumerable<string> SegmentValues(string headers) =>
        headers.Split(',')
            .Select(segment => segment.IndexOf('=', StringComparison.Ordinal) is var separator and >= 0
                ? segment[(separator + 1)..].Trim()
                : string.Empty)
            .Where(value => value.Length > 0);

    /// <summary>
    /// Returns whether <paramref name="name"/> is a non-empty HTTP header token.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns><see langword="true"/> when every character is an ASCII letter, digit or token symbol.</returns>
    private static bool IsToken(string name) =>
        name.Length > 0 && name.All(character => char.IsAsciiLetterOrDigit(character) || _tokenSymbols.Contains(character));

    /// <summary>
    /// Returns whether <paramref name="value"/> may be sent as an HTTP header value.
    /// </summary>
    /// <param name="value">The header value.</param>
    /// <returns><see langword="true"/> when the value is not empty or white space and holds no control characters.</returns>
    private static bool IsHeaderValue(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
}
