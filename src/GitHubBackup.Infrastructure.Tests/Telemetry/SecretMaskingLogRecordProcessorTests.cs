using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Telemetry;
using GitHubBackup.TestUtilities.Telemetry;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace GitHubBackup.Infrastructure.Tests.Telemetry;

/// <summary>
/// Tests <see cref="SecretMaskingLogRecordProcessor"/> in an OpenTelemetry logging pipeline followed by an exporter.
/// </summary>
public sealed class SecretMaskingLogRecordProcessorTests : IDisposable
{
    /// <summary>The secret registered with the masker of the pipeline.</summary>
    private const string _secret = ProcessorTestLog.Secret;

    /// <summary>Receives the records after the processor.</summary>
    private readonly CapturingLogExporter _exporter = new();

    /// <summary>The factory of the pipeline under test.</summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>The logger that writes into the pipeline.</summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Builds a pipeline with the processor, then a synchronous export into <see cref="_exporter"/>.
    /// </summary>
    public SecretMaskingLogRecordProcessorTests()
    {
        var masker = new SecretMasker();
        masker.Register(_secret);
        _loggerFactory = LoggerFactory.Create(logging => logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;
            options.AddProcessor(new SecretMaskingLogRecordProcessor(masker));
            options.AddProcessor(new SimpleLogRecordExportProcessor(_exporter));
        }));
        _logger = _loggerFactory.CreateLogger("Test");
    }

    [Fact]
    public void OnEnd_TokenInMessage_ExportsStars()
    {
        ProcessorTestLog.HeaderSent(_logger);

        var record = _exporter.Records.ShouldHaveSingleItem();
        record.FormattedMessage.ShouldBe("Header *** sent");
        record.Body.ShouldBe("Header *** sent");
        record.GetAllTexts().ShouldAllBe(text => !text.Contains(_secret));
    }

    [Fact]
    public void OnEnd_TokenInAttribute_ExportsStars()
    {
        ProcessorTestLog.ValueAt(_logger, "x " + _secret + " y", 3);

        var record = _exporter.Records.ShouldHaveSingleItem();
        record.GetAttribute("Value").ShouldBe("x *** y");
        record.GetAttribute("Count").ShouldBe(3);
        record.FormattedMessage.ShouldBe("Value x *** y at 3");
        record.GetAllTexts().ShouldAllBe(text => !text.Contains(_secret));
    }

    [Fact]
    public void OnEnd_NonPrimitiveAttribute_ExportsMaskedText()
    {
        var address = new Uri("https://example.com/" + _secret);

        ProcessorTestLog.Address(_logger, address);

        var record = _exporter.Records.ShouldHaveSingleItem();
        record.GetAttribute("Address").ShouldBe("https://example.com/***");
    }

    [Fact]
    public void OnEnd_TokenInExceptionMessage_ExportsStarsInExceptionAttributes()
    {
        var exception = new InvalidOperationException("Rejected " + _secret);

        ProcessorTestLog.RequestFailed(_logger, exception);

        var record = _exporter.Records.ShouldHaveSingleItem();
        record.Exception.ShouldBeNull();
        record.GetAttribute(SecretMaskingLogRecordProcessor.ExceptionTypeAttribute)
            .ShouldBe(typeof(InvalidOperationException).FullName);
        record.GetAttribute(SecretMaskingLogRecordProcessor.ExceptionMessageAttribute).ShouldBe("Rejected ***");
        record.GetAttribute(SecretMaskingLogRecordProcessor.ExceptionStackTraceAttribute)
            .ShouldBe($"{typeof(InvalidOperationException).FullName}: Rejected ***");
        record.GetAllTexts().ShouldAllBe(text => !text.Contains(_secret));
    }

    [Fact]
    public void OnEnd_NoSecret_KeepsMessageAndAttributes()
    {
        ProcessorTestLog.Cloning(_logger, "octocat/hello-world");

        var record = _exporter.Records.ShouldHaveSingleItem();
        record.LogLevel.ShouldBe(LogLevel.Warning);
        record.FormattedMessage.ShouldBe("Cloning octocat/hello-world");
        record.GetAttribute("Repository").ShouldBe("octocat/hello-world");
        record.GetAttribute("{OriginalFormat}").ShouldBe("Cloning {Repository}");
        record.Exception.ShouldBeNull();
        record.GetAttribute(SecretMaskingLogRecordProcessor.ExceptionTypeAttribute).ShouldBeNull();
    }

    /// <summary>
    /// Disposes the pipeline.
    /// </summary>
    public void Dispose()
    {
        _loggerFactory.Dispose();
        _exporter.Dispose();
    }
}
