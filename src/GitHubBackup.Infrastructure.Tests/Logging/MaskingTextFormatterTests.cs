using System.Globalization;
using System.Text.Json;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Logging;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Display;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="MaskingTextFormatter"/> over the text and the CLEF formatters.
/// </summary>
public sealed class MaskingTextFormatterTests
{
    /// <summary>A registered secret that has no recognisable format.</summary>
    private const string _secret = "s3cret-value-without-format";

    private readonly SecretMasker _masker = new();

    /// <summary>
    /// Registers <see cref="_secret"/> with the masker.
    /// </summary>
    public MaskingTextFormatterTests() => _masker.Register(_secret);

    [Fact]
    public void Format_SecretInMessage_MasksTextOutput()
    {
        var output = Format(TextFormatter(), LogEvents.Create($"Using {_secret} now"));

        output.ShouldNotContain(_secret);
        output.ShouldContain("Using *** now");
    }

    [Fact]
    public void Format_SecretInPropertyAndException_MasksTextOutput()
    {
        var logEvent = LogEvents.Create(
            "Request failed",
            new InvalidOperationException($"Authorization: Bearer {_secret}"),
            ("Header", $"Authorization: token {_secret}"));

        var output = Format(TextFormatter(), logEvent);

        output.ShouldNotContain(_secret);
        output.ShouldContain("Authorization: Bearer ***");
        output.ShouldContain("Authorization: token ***");
    }

    [Fact]
    public void Format_SecretInMessagePropertyAndException_MasksJsonOutputAndKeepsItValid()
    {
        var logEvent = LogEvents.Create(
            "Calling with {Token}",
            new InvalidOperationException($"Failed with {_secret}"),
            ("Token", _secret),
            ("Header", "Authorization: Basic dGVzdC12YWx1ZQ=="));

        var output = Format(new CompactJsonFormatter(), logEvent);

        output.ShouldNotContain(_secret);
        output.ShouldNotContain("dGVzdC12YWx1ZQ==");
        using var document = JsonDocument.Parse(output);
        document.RootElement.GetProperty("Token").GetString().ShouldBe(SecretMasker.Replacement);
        document.RootElement.GetProperty("Header").GetString().ShouldBe("Authorization: Basic ***");
        document.RootElement.GetProperty("@x").GetString().ShouldNotBeNull().ShouldContain("Failed with ***");
    }

    [Fact]
    public void Format_NoSecret_WritesInnerOutputUnchanged()
    {
        var logEvent = LogEvents.Create("Cloning {Repository}", null, ("Repository", "octocat/hello-world"));

        Format(TextFormatter(), logEvent).ShouldBe(FormatUnmasked(TextFormatter(), logEvent));
    }

    /// <summary>
    /// Creates the text formatter with the layout of the text log.
    /// </summary>
    /// <returns>The formatter.</returns>
    private static MessageTemplateTextFormatter TextFormatter() =>
        new(FileLoggingServiceCollectionExtensions.TextOutputTemplate, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats <paramref name="logEvent"/> with <paramref name="inner"/> without masking.
    /// </summary>
    /// <param name="inner">The formatter.</param>
    /// <param name="logEvent">The entry.</param>
    /// <returns>The rendered entry.</returns>
    private static string FormatUnmasked(MessageTemplateTextFormatter inner, Serilog.Events.LogEvent logEvent)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        inner.Format(logEvent, output);
        return output.ToString();
    }

    /// <summary>
    /// Formats <paramref name="logEvent"/> through a <see cref="MaskingTextFormatter"/> over <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The formatter that renders the entry.</param>
    /// <param name="logEvent">The entry.</param>
    /// <returns>The masked output.</returns>
    private string Format(ITextFormatter inner, Serilog.Events.LogEvent logEvent)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        new MaskingTextFormatter(inner, _masker).Format(logEvent, output);
        return output.ToString();
    }
}
