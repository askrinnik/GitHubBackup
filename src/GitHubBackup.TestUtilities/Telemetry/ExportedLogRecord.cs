using Microsoft.Extensions.Logging;

namespace GitHubBackup.TestUtilities.Telemetry;

/// <summary>
/// Holds the fields of an OpenTelemetry log record as an exporter received them; the SDK reuses the original record.
/// </summary>
/// <param name="CategoryName">The category of the logger that wrote the entry.</param>
/// <param name="LogLevel">The level of the entry.</param>
/// <param name="FormattedMessage">The rendered message, or <see langword="null"/> when it is not included.</param>
/// <param name="Body">The message template, or <see langword="null"/>.</param>
/// <param name="Attributes">The attributes, in order.</param>
/// <param name="Exception">The exception attached to the record, or <see langword="null"/>.</param>
public sealed record ExportedLogRecord(
    string? CategoryName,
    LogLevel LogLevel,
    string? FormattedMessage,
    string? Body,
    IReadOnlyList<KeyValuePair<string, object?>> Attributes,
    Exception? Exception)
{
    /// <summary>
    /// Returns the value of the attribute <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The attribute key.</param>
    /// <returns>The value, or <see langword="null"/> when the record has no such attribute.</returns>
    public object? GetAttribute(string key) =>
        Attributes.FirstOrDefault(attribute => attribute.Key == key).Value;

    /// <summary>
    /// Returns every text the record exports: the message, the body, and each attribute value as text.
    /// </summary>
    /// <returns>The texts.</returns>
    public IEnumerable<string> GetAllTexts() =>
        [FormattedMessage ?? string.Empty, Body ?? string.Empty, .. Attributes.Select(attribute => attribute.Value?.ToString() ?? string.Empty)];
}
