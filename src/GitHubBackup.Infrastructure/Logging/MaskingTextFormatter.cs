using System.Globalization;
using GitHubBackup.Core.Security;
using Serilog.Events;
using Serilog.Formatting;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Formats a log entry with another formatter and masks secrets in the result before it reaches the file.
/// </summary>
/// <remarks>
/// Masking the formatted text covers the message, every property and the exception text in one place, whatever
/// the inner formatter renders.
/// </remarks>
/// <param name="inner">The formatter that renders the entry.</param>
/// <param name="masker">The masker applied to the rendered entry.</param>
internal sealed class MaskingTextFormatter(ITextFormatter inner, ISecretMasker masker) : ITextFormatter
{
    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        inner.Format(logEvent, buffer);
        output.Write(masker.Mask(buffer.ToString()));
    }
}
