using System.Text.Json;
using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Runs;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.IntegrationTests.Logging;

/// <summary>
/// Tests the Serilog file logging of the shared host against real files in a temporary content root.
/// </summary>
public sealed class SerilogFileLoggingTests
{
    /// <summary>A settings file that sets only the default level.</summary>
    private const string _informationAppSettings = """{ "Serilog": { "MinimumLevel": { "Default": "Information" } } }""";

    /// <summary>A token registered through configuration; it has no recognisable format.</summary>
    private const string _configuredToken = "configured-token-without-format";

    /// <summary>A token in the classic GitHub format that is not registered anywhere.</summary>
    private const string _unregisteredToken = "ghp_TestTokenValue0123456789abcdefABCDEF";

    [Fact]
    public void Log_Entry_WritesBothDailyFilesInLogsFolder()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);
        var before = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);

        WriteEntry(contentRoot, logger => TestLog.Write(logger, LogLevel.Information, "Entry"));

        var after = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var fileNames = Directory.GetFiles(contentRoot.LogsPath).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        new[] { ExpectedFileNames(before), ExpectedFileNames(after) }.ShouldContain(expected => expected.SequenceEqual(fileNames));
    }

    [Fact]
    public void Log_Entry_WritesRunIdOfHostToBothFiles()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);
        string runId;
        using (var host = contentRoot.BuildHost())
        {
            runId = host.Services.GetRequiredService<IRunContext>().RunId.ToString("D");
            var logger = CreateLogger(host);
            TestLog.Write(logger, LogLevel.Information, "Entry");
        }

        var entry = ReadJsonEntries(contentRoot).ShouldHaveSingleItem();
        entry.GetProperty(LogProperties.RunId).GetString().ShouldBe(runId);
        ReadText(contentRoot).ShouldContain($"] {runId} ");
    }

    [Fact]
    public void Log_SecretsInMessagePropertiesAndException_AreMaskedInBothFiles()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);

        WriteEntry(
            contentRoot,
            logger => TestLog.CallFailed(
                logger,
                new InvalidOperationException($"Request with Authorization: Bearer opaque-bearer-value failed for {_configuredToken}"),
                _configuredToken,
                _unregisteredToken,
                "Authorization: Basic b3BhcXVlLXZhbHVl"),
            $"--GitHub:Token={_configuredToken}");

        foreach (var content in new[] { ReadText(contentRoot), File.ReadAllText(LogFilePath(contentRoot, LogFiles.JsonExtension)) })
        {
            content.ShouldNotContain(_configuredToken);
            content.ShouldNotContain(_unregisteredToken);
            content.ShouldNotContain("opaque-bearer-value");
            content.ShouldNotContain("b3BhcXVlLXZhbHVl");
            content.ShouldContain("Authorization: Basic ***");
        }

        var entry = ReadJsonEntries(contentRoot).ShouldHaveSingleItem();
        entry.GetProperty("Token").GetString().ShouldBe("***");
        entry.GetProperty("@x").GetString().ShouldStartWith(
            "System.InvalidOperationException: Request with Authorization: Bearer *** failed for ***");
    }

    [Fact]
    public void Log_InsideRepositoryAndOperationScopes_WritesBothPropertiesToBothFiles()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);

        WriteEntry(contentRoot, logger =>
        {
            using var repositoryScope = logger.BeginRepositoryScope("octocat/hello-world");
            using var operationScope = logger.BeginOperationScope("clone");
            TestLog.Write(logger, LogLevel.Information, "Entry");
        });

        var entry = ReadJsonEntries(contentRoot).ShouldHaveSingleItem();
        entry.GetProperty(LogProperties.Repository).GetString().ShouldBe("octocat/hello-world");
        entry.GetProperty(LogProperties.Operation).GetString().ShouldBe("clone");
        var text = ReadText(contentRoot);
        text.ShouldContain("\"Repository\":\"octocat/hello-world\"");
        text.ShouldContain("\"Operation\":\"clone\"");
    }

    [Fact]
    public void Log_DefaultLevelAndOverride_WritesOnlyEntriesAtOrAboveTheirLevel()
    {
        using var contentRoot = new TemporaryContentRoot(
            """{ "Serilog": { "MinimumLevel": { "Default": "Warning", "Override": { "Verbose.Source": "Debug" } } } }""");

        using (var host = contentRoot.BuildHost())
        {
            var factory = host.Services.GetRequiredService<ILoggerFactory>();
            var other = factory.CreateLogger("Other.Source");
            var verbose = factory.CreateLogger("Verbose.Source.Component");
            TestLog.Write(other, LogLevel.Information, "Other information");
            TestLog.Write(other, LogLevel.Warning, "Other warning");
            TestLog.Write(verbose, LogLevel.Trace, "Verbose trace");
            TestLog.Write(verbose, LogLevel.Debug, "Verbose debug");
        }

        ReadJsonEntries(contentRoot).Select(entry => entry.GetProperty("Text").GetString())
            .ShouldBe(["Other warning", "Verbose debug"]);
    }

    [Fact]
    public void Build_InvalidLevel_ThrowsValidationExceptionNamingKey()
    {
        using var contentRoot = new TemporaryContentRoot("""{ "Serilog": { "MinimumLevel": { "Default": "Loud" } } }""");

        var exception = Should.Throw<OptionsValidationException>(() => contentRoot.BuildHost().Dispose());

        exception.Failures.ShouldHaveSingleItem().ShouldStartWith("Serilog:MinimumLevel:Default must be one of ");
    }

    [Fact]
    public void Dispose_Host_FlushesAndReleasesLogFiles()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);

        WriteEntry(contentRoot, logger => TestLog.Write(logger, LogLevel.Information, "Entry"));

        foreach (var extension in new[] { LogFiles.TextExtension, LogFiles.JsonExtension })
        {
            using var exclusive = new FileStream(LogFilePath(contentRoot, extension), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            exclusive.Length.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void Log_ManyOlderLogFiles_KeepsThemAll()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);
        Directory.CreateDirectory(contentRoot.LogsPath);
        var oldFiles = Enumerable.Range(1, 40)
            .SelectMany(day => new[]
            {
                LogFiles.GetFileName(new DateOnly(2000, 1, 1).AddDays(day), LogFiles.TextExtension),
                LogFiles.GetFileName(new DateOnly(2000, 1, 1).AddDays(day), LogFiles.JsonExtension),
            })
            .Select(name => Path.Combine(contentRoot.LogsPath, name))
            .ToArray();
        foreach (var file in oldFiles)
        {
            File.WriteAllText(file, "old");
        }

        WriteEntry(contentRoot, logger => TestLog.Write(logger, LogLevel.Information, "Entry"));

        oldFiles.ShouldAllBe(file => File.Exists(file));
    }

    /// <summary>
    /// Returns the two file names expected for <paramref name="date"/>, in ordinal order.
    /// </summary>
    /// <param name="date">The local date.</param>
    /// <returns>The JSON and the text file names.</returns>
    private static string[] ExpectedFileNames(DateOnly date) =>
        [LogFiles.GetFileName(date, LogFiles.JsonExtension), LogFiles.GetFileName(date, LogFiles.TextExtension)];

    /// <summary>
    /// Builds a host over <paramref name="contentRoot"/>, lets <paramref name="write"/> log through it and disposes
    /// the host, which closes the files.
    /// </summary>
    /// <param name="contentRoot">The content root of the host.</param>
    /// <param name="write">Writes the entries.</param>
    /// <param name="args">The command-line arguments.</param>
    private static void WriteEntry(TemporaryContentRoot contentRoot, Action<ILogger> write, params string[] args)
    {
        using var host = contentRoot.BuildHost(args);
        write(CreateLogger(host));
    }

    /// <summary>
    /// Creates the logger of this test class from the host.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>The logger.</returns>
    private static ILogger<SerilogFileLoggingTests> CreateLogger(IHost host) =>
        host.Services.GetRequiredService<ILogger<SerilogFileLoggingTests>>();

    /// <summary>
    /// Returns the path of the only log file with <paramref name="extension"/>.
    /// </summary>
    /// <param name="contentRoot">The content root of the host.</param>
    /// <param name="extension">The extension of the file.</param>
    /// <returns>The path.</returns>
    private static string LogFilePath(TemporaryContentRoot contentRoot, string extension) =>
        Directory.GetFiles(contentRoot.LogsPath, LogFiles.FileNamePrefix + "*" + extension).ShouldHaveSingleItem();

    /// <summary>
    /// Reads the text log.
    /// </summary>
    /// <param name="contentRoot">The content root of the host.</param>
    /// <returns>The content of the file.</returns>
    private static string ReadText(TemporaryContentRoot contentRoot) =>
        File.ReadAllText(LogFilePath(contentRoot, LogFiles.TextExtension));

    /// <summary>
    /// Reads the entries of the structured log.
    /// </summary>
    /// <param name="contentRoot">The content root of the host.</param>
    /// <returns>One element per entry.</returns>
    private static JsonElement[] ReadJsonEntries(TemporaryContentRoot contentRoot) =>
        [.. File.ReadAllLines(LogFilePath(contentRoot, LogFiles.JsonExtension)).Select(ParseEntry)];

    /// <summary>
    /// Parses one line of the structured log.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The entry, independent of the parsed document.</returns>
    private static JsonElement ParseEntry(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }
}
