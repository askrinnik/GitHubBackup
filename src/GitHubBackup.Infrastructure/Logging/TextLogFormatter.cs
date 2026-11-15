using System.Globalization;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Renders one entry of the text log in the layout of <see cref="Layout"/>, followed by the entry's remaining
/// properties as JSON only when it has any.
/// </summary>
/// <remarks>
/// Serilog's <c>{Properties:j}</c> token prints <c>{}</c> for an entry whose properties all appear in the message
/// or the layout. A property is remaining when neither the message template nor the layout names it, which is the
/// rule <c>{Properties}</c> itself applies, so an entry that has remaining properties renders exactly as it would with
/// the token in the layout.
/// </remarks>
internal sealed class TextLogFormatter : ITextFormatter
{
    /// <summary>The layout of one entry, without the remaining properties.</summary>
    internal const string Layout =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {RunId} {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>The layout of one entry with the remaining properties after the message.</summary>
    private const string _layoutWithProperties =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {RunId} {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}";

    /// <summary>The names of the properties that <see cref="Layout"/> renders itself.</summary>
    private static readonly HashSet<string> _layoutPropertyNames =
        [.. new MessageTemplateParser().Parse(Layout).Tokens.OfType<PropertyToken>().Select(token => token.PropertyName)];

    private readonly MessageTemplateTextFormatter _withoutProperties = new(Layout, CultureInfo.InvariantCulture);
    private readonly MessageTemplateTextFormatter _withProperties = new(_layoutWithProperties, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output) =>
        (HasRemainingProperties(logEvent) ? _withProperties : _withoutProperties).Format(logEvent, output);

    /// <summary>
    /// Returns whether <paramref name="logEvent"/> has a property that neither its message template nor the layout
    /// names.
    /// </summary>
    /// <param name="logEvent">The entry.</param>
    /// <returns><see langword="true"/> when the entry has a remaining property.</returns>
    private static bool HasRemainingProperties(LogEvent logEvent)
    {
        var messagePropertyNames = logEvent.MessageTemplate.Tokens.OfType<PropertyToken>().Select(token => token.PropertyName);
        return logEvent.Properties.Keys.Except(_layoutPropertyNames).Except(messagePropertyNames).Any();
    }
}
