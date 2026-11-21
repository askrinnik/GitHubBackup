using GitHubBackup.Core.Security;
using Serilog.Core;
using Serilog.Events;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Writes entries to a <see cref="TextWriter"/> in the layout of the text log file, with secrets masked and without
/// exception stack traces.
/// </summary>
/// <remarks>
/// The console shares stderr with the one-line error messages of the program, so an entry with an exception shows
/// only the exception type and message on the line after the entry; the full exception stays in the log file. Each
/// entry is written and flushed under a lock, so entries from several threads never interleave and a console shows
/// an entry as soon as it is logged.
/// </remarks>
/// <param name="writer">The writer that receives the entries.</param>
/// <param name="masker">The masker applied to every rendered entry.</param>
internal sealed class TextWriterLogSink(TextWriter writer, ISecretMasker masker) : ILogEventSink
{
    /// <summary>The formatter of the text log, wrapped in masking.</summary>
    private readonly MaskingTextFormatter _formatter = new(new TextLogFormatter(), masker);

    /// <summary>Serializes the writes to the writer.</summary>
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        var exception = logEvent.Exception;
        var entry = exception is null
            ? logEvent
            : new LogEvent(
                logEvent.Timestamp,
                logEvent.Level,
                null,
                logEvent.MessageTemplate,
                logEvent.Properties.Select(property => new LogEventProperty(property.Key, property.Value)));

        lock (_lock)
        {
            _formatter.Format(entry, writer);
            if (exception is not null)
            {
                writer.WriteLine(masker.Mask($"{exception.GetType().FullName}: {exception.Message}"));
            }

            writer.Flush();
        }
    }
}
