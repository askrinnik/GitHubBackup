using System.Collections.Concurrent;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace GitHubBackup.TestUtilities.Telemetry;

/// <summary>
/// Keeps a copy of every log record it exports, so a test can read what an OTLP exporter in the same pipeline would
/// send.
/// </summary>
public sealed class CapturingLogExporter : BaseExporter<LogRecord>
{
    /// <summary>The copies of the exported records, in export order.</summary>
    private readonly ConcurrentQueue<ExportedLogRecord> _records = new();

    /// <summary>Gets the copies of the exported records, in export order.</summary>
    public IReadOnlyList<ExportedLogRecord> Records => [.. _records];

    /// <inheritdoc />
    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (var record in batch)
        {
            _records.Enqueue(new(
                record.CategoryName,
                record.LogLevel,
                record.FormattedMessage,
                record.Body,
                [.. record.Attributes ?? []],
                record.Exception));
        }

        return ExportResult.Success;
    }
}
