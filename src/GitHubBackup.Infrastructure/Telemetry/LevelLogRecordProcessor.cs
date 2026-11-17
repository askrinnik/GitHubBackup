using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace GitHubBackup.Infrastructure.Telemetry;

/// <summary>
/// Adds the level of a log record as the <c>level</c> attribute, so a backend that lists attributes shows the level
/// next to the other properties of the entry.
/// </summary>
/// <remarks>
/// The value is the name of <see cref="LogRecord.LogLevel"/>, which is the severity text of the record; the SDK does
/// not expose the severity text itself. A record at <see cref="LogLevel.None"/> gets no attribute, and a record that
/// already carries <c>level</c> keeps its own value. The key is compared ordinally because <c>Level</c> and
/// <c>level</c> are different attributes in OTLP. The level comes from a fixed list and needs no masking, so this
/// processor runs after <see cref="SecretMaskingLogRecordProcessor"/>.
/// </remarks>
internal sealed class LevelLogRecordProcessor : BaseProcessor<LogRecord>
{
    /// <summary>The attribute that carries the level of the record.</summary>
    internal const string LevelAttribute = "level";

    /// <inheritdoc />
    public override void OnEnd(LogRecord data)
    {
        if (data.LogLevel is LogLevel.None)
        {
            return;
        }

        var existing = data.Attributes ?? [];
        if (existing.Any(attribute => string.Equals(attribute.Key, LevelAttribute, StringComparison.Ordinal)))
        {
            return;
        }

        var attributes = new List<KeyValuePair<string, object?>>(existing.Count + 1);
        attributes.AddRange(existing);
        attributes.Add(new(LevelAttribute, data.LogLevel.ToString()));
        data.Attributes = attributes;
    }
}
