using System.Globalization;
using GitHubBackup.Core.Security;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace GitHubBackup.Infrastructure.Telemetry;

/// <summary>
/// Masks the secrets of a log record before the exporters that follow it in the pipeline read the record.
/// </summary>
/// <remarks>
/// Processors run in the order they are registered, and an exporter keeps the record it is given, so this processor
/// is registered before the exporter. The exception leaves the record: the exporter would write its message and stack
/// trace unmasked, so they travel as masked <c>exception.*</c> attributes instead.
/// </remarks>
/// <param name="secretMasker">The masker shared with the log files, which also knows the secrets registered at run time.</param>
internal sealed class SecretMaskingLogRecordProcessor(ISecretMasker secretMasker) : BaseProcessor<LogRecord>
{
    /// <summary>The attribute that carries the full type name of the exception.</summary>
    internal const string ExceptionTypeAttribute = "exception.type";

    /// <summary>The attribute that carries the masked message of the exception.</summary>
    internal const string ExceptionMessageAttribute = "exception.message";

    /// <summary>The attribute that carries the masked text of the exception, including its stack trace.</summary>
    internal const string ExceptionStackTraceAttribute = "exception.stacktrace";

    /// <inheritdoc />
    public override void OnEnd(LogRecord data)
    {
        if (data.FormattedMessage is { } message)
        {
            data.FormattedMessage = secretMasker.Mask(message);
        }

        if (data.Body is { } body)
        {
            data.Body = secretMasker.Mask(body);
        }

        var attributes = new List<KeyValuePair<string, object?>>((data.Attributes?.Count ?? 0) + 3);
        foreach (var (key, value) in data.Attributes ?? [])
        {
            attributes.Add(new(key, MaskValue(value)));
        }

        if (data.Exception is { } exception)
        {
            attributes.Add(new(ExceptionTypeAttribute, exception.GetType().FullName));
            attributes.Add(new(ExceptionMessageAttribute, secretMasker.Mask(exception.Message)));
            attributes.Add(new(ExceptionStackTraceAttribute, secretMasker.Mask(exception.ToString())));
            data.Exception = null;
        }

        data.Attributes = attributes;
    }

    /// <summary>
    /// Returns <paramref name="value"/> with secrets masked: a primitive value as it is, any other value as its masked
    /// text.
    /// </summary>
    /// <remarks>
    /// The exporter writes an object it does not know as its text, and the text of a structured value can hold a
    /// secret, so every value that is not a primitive becomes masked text here.
    /// </remarks>
    /// <param name="value">The value of an attribute.</param>
    /// <returns>The value to export.</returns>
    private object? MaskValue(object? value) => value switch
    {
        null => null,
        string text => secretMasker.Mask(text),
        _ when value.GetType().IsPrimitive => value,
        _ => secretMasker.Mask(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
    };
}
