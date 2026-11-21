using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Logging;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="TextWriterLogSink"/>.
/// </summary>
public sealed class TextWriterLogSinkTests : IDisposable
{
    /// <summary>A registered secret that has no recognisable format.</summary>
    private const string _secret = "s3cret-value-without-format";

    private readonly SecretMasker _masker = new();
    private readonly StringWriter _writer = new();

    /// <summary>
    /// Registers <see cref="_secret"/> with the masker.
    /// </summary>
    public TextWriterLogSinkTests() => _masker.Register(_secret);

    [Fact]
    public void Emit_Entry_WritesLineInTextLogLayout()
    {
        var sink = new TextWriterLogSink(_writer, _masker);

        sink.Emit(LogEvents.Create(
            "Cloning {Repository}", null, ("Repository", "octocat/hello"), ("RunId", "run-1"), ("SourceContext", "Source")));

        _writer.ToString().ShouldBe(
            $"2026-10-07 12:30:00.000 +00:00 [INF] run-1 Source: Cloning octocat/hello{Environment.NewLine}");
    }

    [Fact]
    public void Emit_SecretInMessageAndProperty_WritesStars()
    {
        var sink = new TextWriterLogSink(_writer, _masker);

        sink.Emit(LogEvents.Create(
            $"Using {_secret} with {{Token}}", null, ("Token", _secret), ("RunId", "run-1"), ("SourceContext", "Source")));

        _writer.ToString().ShouldNotContain(_secret);
        _writer.ToString().ShouldBe(
            $"2026-10-07 12:30:00.000 +00:00 [INF] run-1 Source: Using *** with ***{Environment.NewLine}");
    }

    [Fact]
    public void Emit_EntryWithException_WritesTypeAndMessageWithoutStackTrace()
    {
        var sink = new TextWriterLogSink(_writer, _masker);
        var exception = CreateThrownException($"Bad value {_secret}");

        sink.Emit(LogEvents.Create("Hosting failed to start", exception, ("RunId", "run-1"), ("SourceContext", "Source")));

        _writer.ToString().ShouldBe(
            $"2026-10-07 12:30:00.000 +00:00 [INF] run-1 Source: Hosting failed to start{Environment.NewLine}" +
            $"System.InvalidOperationException: Bad value ***{Environment.NewLine}");
    }

    /// <summary>
    /// Throws and catches an exception, so that it carries a stack trace.
    /// </summary>
    /// <param name="message">The message of the exception.</param>
    /// <returns>The caught exception.</returns>
    private static InvalidOperationException CreateThrownException(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    /// <summary>
    /// Disposes the writer.
    /// </summary>
    public void Dispose() => _writer.Dispose();
}
