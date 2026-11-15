using System.Globalization;
using GitHubBackup.Infrastructure.Logging;
using Serilog.Events;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="TextLogFormatter"/>: the remaining properties after the message
/// appear only when an entry has any.
/// </summary>
public sealed class TextLogFormatterTests
{
    /// <summary>The rendered start of every entry that has no <c>RunId</c> and no <c>SourceContext</c>.</summary>
    private const string _prefix = "[INF]  : ";

    [Fact]
    public void Format_NoProperties_EndsWithMessage()
    {
        var output = Format(LogEvents.Create("Run started"));

        output.ShouldEndWith(_prefix + "Run started" + Environment.NewLine);
        output.ShouldNotContain("{}");
    }

    [Fact]
    public void Format_PropertyOnlyInMessage_EndsWithMessage()
    {
        var output = Format(LogEvents.Create("Cloning {Repository}", null, ("Repository", "octo/app")));

        output.ShouldEndWith(_prefix + "Cloning octo/app" + Environment.NewLine);
    }

    [Fact]
    public void Format_PropertiesOnlyInLayout_EndsWithMessage()
    {
        var output = Format(LogEvents.Create("Run started", null, ("RunId", "run-1"), ("SourceContext", "Octo.Source")));

        output.ShouldEndWith("[INF] run-1 Octo.Source: Run started" + Environment.NewLine);
    }

    [Fact]
    public void Format_RemainingProperties_AppendsThemAsJson()
    {
        var output = Format(LogEvents.Create(
            "Cloning {Repository}", null, ("Repository", "octo/app"), ("Operation", "clone"), ("Tool", "git")));

        output.ShouldEndWith(_prefix + """Cloning octo/app {"Operation":"clone","Tool":"git"}""" + Environment.NewLine);
    }

    [Fact]
    public void Format_ExceptionWithoutRemainingProperties_WritesExceptionAfterMessageLine()
    {
        var output = Format(LogEvents.Create("Run failed", new InvalidOperationException("Broken")));

        output.ShouldContain(_prefix + "Run failed" + Environment.NewLine + "System.InvalidOperationException: Broken");
        output.ShouldNotContain("{}");
    }

    /// <summary>
    /// Formats <paramref name="logEvent"/> with a new formatter.
    /// </summary>
    /// <param name="logEvent">The entry.</param>
    /// <returns>The rendered entry.</returns>
    private static string Format(LogEvent logEvent)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        new TextLogFormatter().Format(logEvent, output);
        return output.ToString();
    }
}
