using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Runs;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Telemetry;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace GitHubBackup.Infrastructure.IntegrationTests.Logging;

/// <summary>
/// Tests that the shared host with telemetry enabled hands its log entries to the OpenTelemetry pipeline after the
/// Serilog levels and with secrets masked, as the OTLP exporter receives them.
/// </summary>
public sealed class OtlpLogExportTests : IDisposable
{
    /// <summary>A settings file that writes entries from the information level.</summary>
    private const string _informationAppSettings = """{ "Serilog": { "MinimumLevel": { "Default": "Information" } } }""";

    /// <summary>The switch that enables the telemetry.</summary>
    private const string _enabledArgument = "--OpenTelemetry:Enabled=true";

    /// <summary>
    /// An endpoint where nothing listens, so the OTLP exporter of the host never reaches a collector that may run on
    /// the machine.
    /// </summary>
    private const string _endpointArgument = "--OpenTelemetry:OtlpEndpoint=http://127.0.0.1:1";

    /// <summary>A token registered through configuration; it has no recognisable format.</summary>
    private const string _configuredToken = "configured-token-without-format";

    /// <summary>A token in the classic GitHub format that is not registered anywhere.</summary>
    private const string _unregisteredToken = "ghp_TestTokenValue0123456789abcdefABCDEF";

    /// <summary>The category of the entries the tests write.</summary>
    private static readonly string _category = typeof(OtlpLogExportTests).FullName!;

    /// <summary>Receives the records after the masking processor and the OTLP exporter of the host.</summary>
    private readonly CapturingLogExporter _exporter = new();

    [Fact]
    public void Log_TelemetryEnabled_ExportsMessageLevelAndContext()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);
        string runId;

        using (var host = BuildHost(contentRoot, _enabledArgument, _endpointArgument))
        {
            runId = host.Services.GetRequiredService<IRunContext>().RunId.ToString("D");
            var logger = CreateLogger(host);
            using var repositoryScope = logger.BeginRepositoryScope("octocat/hello-world");
            using var operationScope = logger.BeginOperationScope("clone");
            TestLog.Write(logger, LogLevel.Warning, "Entry");
        }

        var record = OwnRecords().ShouldHaveSingleItem();
        record.LogLevel.ShouldBe(LogLevel.Warning);
        record.FormattedMessage.ShouldBe("\"Entry\"");
        record.GetAttribute(LogProperties.RunId).ShouldBe(runId);
        record.GetAttribute(LogProperties.Repository).ShouldBe("octocat/hello-world");
        record.GetAttribute(LogProperties.Operation).ShouldBe("clone");
    }

    [Fact]
    public void Log_MinimumLevelWarning_ExportsOnlyWarningAndAbove()
    {
        using var contentRoot = new TemporaryContentRoot("""{ "Serilog": { "MinimumLevel": { "Default": "Warning" } } }""");

        WriteEntries(contentRoot);

        OwnRecords().Select(record => record.LogLevel).ShouldBe([LogLevel.Warning, LogLevel.Error]);
    }

    [Fact]
    public void Log_MinimumLevelOverride_AppliesToExport()
    {
        using var contentRoot = new TemporaryContentRoot(
            $$"""{ "Serilog": { "MinimumLevel": { "Default": "Information", "Override": { "{{_category}}": "Error" } } } }""");

        WriteEntries(contentRoot);

        OwnRecords().Select(record => record.LogLevel).ShouldBe([LogLevel.Error]);
    }

    [Fact]
    public void Log_TokenInMessagePropertyAndException_ExportsStars()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);
        var exception = new InvalidOperationException($"Request failed for {_configuredToken} and {_unregisteredToken}");

        using (var host = BuildHost(
            contentRoot, _enabledArgument, _endpointArgument, $"--GitHub:Token={_configuredToken}"))
        {
            TestLog.CallFailed(
                CreateLogger(host),
                exception,
                _configuredToken,
                _unregisteredToken,
                "Authorization: Basic b3BhcXVlLXZhbHVl");
        }

        var record = OwnRecords().ShouldHaveSingleItem();
        foreach (var text in record.GetAllTexts())
        {
            text.ShouldNotContain(_configuredToken);
            text.ShouldNotContain(_unregisteredToken);
            text.ShouldNotContain("b3BhcXVlLXZhbHVl");
        }

        record.Exception.ShouldBeNull();
        record.GetAttribute("Token").ShouldBe("***");
        record.GetAttribute("Header").ShouldBe("Authorization: Basic ***");
        record.GetAttribute(SecretMaskingLogRecordProcessor.ExceptionMessageAttribute)
            .ShouldBe("Request failed for *** and ***");
        record.FormattedMessage.ShouldBe("""Calling with "***", "***" and "Authorization: Basic ***" """.TrimEnd());
    }

    [Fact]
    public void Log_TelemetryEnabled_FilesStillWritten()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);

        using (var host = BuildHost(contentRoot, _enabledArgument, _endpointArgument))
        {
            var logger = CreateLogger(host);
            TestLog.Write(logger, LogLevel.Information, "Entry for files and export");
        }

        contentRoot.ReadJsonLogEntries().ShouldHaveSingleItem().GetProperty("Text").GetString()
            .ShouldBe("Entry for files and export");
        contentRoot.ReadTextLog().ShouldContain("Entry for files and export");
        OwnRecords().ShouldHaveSingleItem().FormattedMessage.ShouldBe("\"Entry for files and export\"");
    }

    /// <summary>
    /// Disposes the exporter.
    /// </summary>
    public void Dispose() => _exporter.Dispose();

    /// <summary>
    /// Builds a host over <paramref name="contentRoot"/> whose OpenTelemetry logging pipeline ends in
    /// <see cref="_exporter"/>, after the processors of the host.
    /// </summary>
    /// <param name="contentRoot">The content root.</param>
    /// <param name="configurationArguments">The configuration keys, each in the form <c>--Section:Key=value</c>.</param>
    /// <returns>The host; the caller disposes it.</returns>
    private IHost BuildHost(TemporaryContentRoot contentRoot, params string[] configurationArguments)
    {
        var builder = GitHubBackupHostBuilder.Create(new()
        {
            ConfigurationArguments = configurationArguments,
            ContentRootPath = contentRoot.FolderPath,
            EnvironmentName = Environments.Production,
        });
        builder.Services.ConfigureOpenTelemetryLoggerProvider(
            logging => logging.AddProcessor(new SimpleLogRecordExportProcessor(_exporter)));
        return builder.Build();
    }

    /// <summary>
    /// Writes one entry per level from <see cref="LogLevel.Debug"/> to <see cref="LogLevel.Error"/> through a host with
    /// telemetry enabled.
    /// </summary>
    /// <param name="contentRoot">The content root whose settings set the levels.</param>
    private void WriteEntries(TemporaryContentRoot contentRoot)
    {
        using var host = BuildHost(contentRoot, _enabledArgument, _endpointArgument);
        var logger = CreateLogger(host);
        foreach (var level in new[] { LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error })
        {
            var text = level.ToString();
            TestLog.Write(logger, level, text);
        }
    }

    /// <summary>
    /// Returns the exported records the tests wrote, without the entries of the host itself.
    /// </summary>
    /// <returns>The records, in export order.</returns>
    private ExportedLogRecord[] OwnRecords() =>
        [.. _exporter.Records.Where(record => record.CategoryName == _category)];

    /// <summary>
    /// Returns the logger of this test class from the host.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>The logger.</returns>
    private static ILogger<OtlpLogExportTests> CreateLogger(IHost host) =>
        host.Services.GetRequiredService<ILogger<OtlpLogExportTests>>();
}
