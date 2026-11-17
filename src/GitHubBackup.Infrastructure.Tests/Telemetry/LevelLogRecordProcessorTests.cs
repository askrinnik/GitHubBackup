using System.Runtime.CompilerServices;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Telemetry;
using GitHubBackup.TestUtilities.Telemetry;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace GitHubBackup.Infrastructure.Tests.Telemetry;

/// <summary>
/// Tests <see cref="LevelLogRecordProcessor"/> in the processor order of the product: masking, level, export.
/// </summary>
public sealed class LevelLogRecordProcessorTests : IDisposable
{
    /// <summary>Receives the records after the processors.</summary>
    private readonly CapturingLogExporter _exporter = new();

    /// <summary>The factory of the pipeline under test.</summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>The logger that writes into the pipeline.</summary>
    private readonly ILogger _logger;

    /// <summary>Tells the test processor to drop the level of the next records.</summary>
    private readonly StrongBox<bool> _dropLevel = new();

    /// <summary>
    /// Builds a pipeline with the processors, then a synchronous export into <see cref="_exporter"/>.
    /// </summary>
    public LevelLogRecordProcessorTests()
    {
        _loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.ParseStateValues = true;
                options.AddProcessor(new SecretMaskingLogRecordProcessor(new SecretMasker()));
                options.AddProcessor(new LevelRemover(_dropLevel));
                options.AddProcessor(new LevelLogRecordProcessor());
                options.AddProcessor(new SimpleLogRecordExportProcessor(_exporter));
            });
        });
        _logger = _loggerFactory.CreateLogger("Test");
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void OnEnd_LogLevel_AddsLevelAttributeWithLevelName(LogLevel level)
    {
        ProcessorTestLog.AtLevel(_logger, level, "octocat/hello-world");

        var record = _exporter.Records.ShouldHaveSingleItem();
        record.Attributes.Count(attribute => attribute.Key == LevelLogRecordProcessor.LevelAttribute).ShouldBe(1);
        record.GetAttribute(LevelLogRecordProcessor.LevelAttribute).ShouldBe(level.ToString());
        record.GetAttribute("Repository").ShouldBe("octocat/hello-world");
    }

    [Fact]
    public void OnEnd_LevelPropertyPresent_KeepsItAndAddsNoSecond()
    {
        ProcessorTestLog.LowerCaseLevelProperty(_logger, "custom");

        var record = _exporter.Records.ShouldHaveSingleItem();
        var levels = record.Attributes.Where(attribute => attribute.Key == "level").ToList();
        levels.Count.ShouldBe(1);
        levels[0].Value.ShouldBe("custom");
    }

    [Fact]
    public void OnEnd_LevelPropertyDiffersInCase_AddsLevelAttribute()
    {
        ProcessorTestLog.UpperCaseLevelProperty(_logger, "custom");

        var record = _exporter.Records.ShouldHaveSingleItem();
        record.GetAttribute("Level").ShouldBe("custom");
        record.GetAttribute("level").ShouldBe("Warning");
    }

    [Fact]
    public void OnEnd_NoLevel_AddsNoLevelAttribute()
    {
        _dropLevel.Value = true;

        ProcessorTestLog.Cloning(_logger, "octocat/hello-world");

        var record = _exporter.Records.ShouldHaveSingleItem();
        record.Attributes.ShouldNotContain(attribute => attribute.Key == LevelLogRecordProcessor.LevelAttribute);
        record.GetAttribute("Repository").ShouldBe("octocat/hello-world");
    }

    [Fact]
    public void OnEnd_ExceptionEntry_KeepsMaskedExceptionAttributesAndAddsLevel()
    {
        ProcessorTestLog.RequestFailed(_logger, new InvalidOperationException("Rejected"));

        var record = _exporter.Records.ShouldHaveSingleItem();
        record.GetAttribute(SecretMaskingLogRecordProcessor.ExceptionTypeAttribute)
            .ShouldBe(typeof(InvalidOperationException).FullName);
        record.GetAttribute(LevelLogRecordProcessor.LevelAttribute).ShouldBe("Error");
    }

    /// <summary>
    /// Disposes the pipeline.
    /// </summary>
    public void Dispose()
    {
        _loggerFactory.Dispose();
        _exporter.Dispose();
    }

    /// <summary>
    /// Sets <see cref="LogRecord.LogLevel"/> to <see cref="LogLevel.None"/> while the switch is on, to model a record without a level.
    /// </summary>
    /// <param name="switchBox">The switch that turns the removal on.</param>
    private sealed class LevelRemover(StrongBox<bool> switchBox) : BaseProcessor<LogRecord>
    {
        /// <inheritdoc />
        public override void OnEnd(LogRecord data)
        {
            if (switchBox.Value)
            {
                data.LogLevel = LogLevel.None;
            }
        }
    }
}
