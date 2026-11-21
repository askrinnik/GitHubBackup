using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Runs;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
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

    /// <summary>A settings file whose endpoints all take the information level, the file included.</summary>
    private const string _informationEverywhereAppSettings =
        """{ "Serilog": { "MinimumLevel": { "Default": "Information" }, "Endpoints": { "File": { "Level": null } } } }""";

    /// <summary>A settings file that lowers the level of the host lifetime messages back to the default level.</summary>
    private const string _lifetimeInformationAppSettings =
        """{ "Serilog": { "MinimumLevel": { "Default": "Information", "Override": { "Microsoft.Hosting.Lifetime": "Information" } } } }""";

    /// <summary>The source of the status messages that the host writes on start and stop.</summary>
    private const string _hostLifetimeSource = "Microsoft.Hosting.Lifetime";

    /// <summary>A token registered through configuration; it has no recognisable format.</summary>
    private const string _configuredToken = "configured-token-without-format";

    /// <summary>A token in the classic GitHub format that is not registered anywhere.</summary>
    private const string _unregisteredToken = "ghp_TestTokenValue0123456789abcdefABCDEF";

    /// <summary>The category of the entries the tests write.</summary>
    private static readonly string _category = typeof(SerilogFileLoggingTests).FullName!;

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

        var entry = contentRoot.ReadJsonLogEntries().ShouldHaveSingleItem();
        entry.GetProperty(LogProperties.RunId).GetString().ShouldBe(runId);
        contentRoot.ReadTextLog().ShouldContain($"] {runId} ");
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

        foreach (var content in new[] { contentRoot.ReadTextLog(), File.ReadAllText(contentRoot.GetLogFilePath(LogFiles.JsonExtension)) })
        {
            content.ShouldNotContain(_configuredToken);
            content.ShouldNotContain(_unregisteredToken);
            content.ShouldNotContain("opaque-bearer-value");
            content.ShouldNotContain("b3BhcXVlLXZhbHVl");
            content.ShouldContain("Authorization: Basic ***");
        }

        var entry = contentRoot.ReadJsonLogEntries().ShouldHaveSingleItem();
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

        var entry = contentRoot.ReadJsonLogEntries().ShouldHaveSingleItem();
        entry.GetProperty(LogProperties.Repository).GetString().ShouldBe("octocat/hello-world");
        entry.GetProperty(LogProperties.Operation).GetString().ShouldBe("clone");
        var text = contentRoot.ReadTextLog();
        text.ShouldContain("\"Repository\":\"octocat/hello-world\"");
        text.ShouldContain("\"Operation\":\"clone\"");
    }

    [Fact]
    public void Log_EntryWithoutRemainingProperties_WritesTextLineWithoutEmptyProperties()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);

        WriteEntry(contentRoot, logger => logger.Log(LogLevel.Information, default, "Entry", null, (state, _) => state));

        var line = contentRoot.ReadTextLog().TrimEnd();
        line.ShouldEndWith($"{typeof(SerilogFileLoggingTests).FullName}: Entry");
        line.ShouldNotContain("{}");
    }

    [Theory]
    [InlineData(_lifetimeInformationAppSettings, true)]
    [InlineData(_informationAppSettings, false)]
    public async Task StartAndStop_HostLifetimeOverride_DecidesWhetherLifetimeMessagesAreLogged(string appSettings, bool expected)
    {
        using var contentRoot = new TemporaryContentRoot(appSettings);
        using (var host = contentRoot.BuildHost())
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        }

        contentRoot.ReadTextLog().Contains(_hostLifetimeSource, StringComparison.Ordinal).ShouldBe(expected);
    }

    [Fact]
    public void Log_DefaultLevelAndOverride_WritesOnlyEntriesAtOrAboveTheirLevel()
    {
        using var contentRoot = new TemporaryContentRoot(
            """
            {
              "Serilog": {
                "MinimumLevel": { "Default": "Warning", "Override": { "Verbose.Source": "Debug" } },
                "Endpoints": { "File": { "Level": null } }
              }
            }
            """);

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

        contentRoot.ReadJsonLogEntries().Select(entry => entry.GetProperty("Text").GetString())
            .ShouldBe(["Other warning", "Verbose debug"]);
    }

    [Fact]
    public void Log_LiveLogFeedSubscribed_ReceivesMaskedEntriesAtFileLevels()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);
        var received = new List<LiveLogEntry>();
        using (var host = BuildLiveLogHost(contentRoot, $"--GitHub:Token={_configuredToken}"))
        {
            var logger = CreateLogger(host);
            using var subscription = host.Services.GetRequiredService<ILiveLogFeed>().Subscribe(received.Add);
            TestLog.Write(logger, LogLevel.Debug, "Below the level");
            TestLog.Write(logger, LogLevel.Warning, $"Calling with {_configuredToken}");
        }

        var entry = received.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe("Calling with ***");
        contentRoot.ReadTextLog().ShouldContain("Calling with ***");
    }

    [Fact]
    public void Log_BeforeSubscribeWithBuffer_FeedMatchesLogFileLevelsOrderAndMasking()
    {
        using var contentRoot = new TemporaryContentRoot(_informationEverywhereAppSettings);
        var received = new List<LiveLogEntry>();
        var builder = GitHubBackupHostBuilder.Create(new()
        {
            ConfigurationArguments = [$"--GitHub:Token={_configuredToken}"],
            ContentRootPath = contentRoot.FolderPath,
            EnvironmentName = Environments.Production,
        });
        builder.Services.AddGitHubBackupLiveLogBuffer(100);
        using (var host = builder.Build())
        {
            var logger = CreateLogger(host);
            TestLog.Write(logger, LogLevel.Information, "Before the window");
            TestLog.Write(logger, LogLevel.Debug, "Below the level");
            TestLog.Write(logger, LogLevel.Warning, $"Calling with {_configuredToken}");
            using var subscription = host.Services.GetRequiredService<ILiveLogFeed>().Subscribe(received.Add);
            TestLog.Write(logger, LogLevel.Error, "After the window opened");
        }

        var fileEntries = contentRoot.ReadJsonLogEntries()
            .Select(entry => (
                Level: entry.TryGetProperty("@l", out var level) ? level.GetString() : nameof(LogLevel.Information),
                Message: entry.GetProperty("Text").GetString()))
            .ToArray();
        fileEntries.ShouldBe(
        [
            (nameof(LogLevel.Information), "Before the window"),
            (nameof(LogLevel.Warning), "Calling with ***"),
            (nameof(LogLevel.Error), "After the window opened"),
        ]);
        received.Select(entry => (entry.Level, entry.Message)).ShouldBe(
        [
            (LogLevel.Information, "Before the window"),
            (LogLevel.Warning, "Calling with ***"),
            (LogLevel.Error, "After the window opened"),
        ]);
    }

    [Fact]
    public void Log_FileDebugAndDefaultWarning_FileReceivesDebugAndLiveLogKeepsDefault()
    {
        using var contentRoot = new TemporaryContentRoot(
            """{ "Serilog": { "MinimumLevel": { "Default": "Warning" }, "Endpoints": { "File": { "Level": "Debug" } } } }""");
        var received = new List<LiveLogEntry>();

        using (var host = BuildLiveLogHost(contentRoot))
        {
            using var subscription = host.Services.GetRequiredService<ILiveLogFeed>().Subscribe(received.Add);
            WriteEachLevel(CreateLogger(host));
        }

        OwnFileTexts(contentRoot).ShouldBe(["Debug", "Information", "Warning", "Error"]);
        received.Select(entry => entry.Message).ShouldBe(["Warning", "Error"]);
    }

    [Fact]
    public void Log_DefaultConfiguration_FileReceivesDebugAndLiveLogOnlyInformation()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);
        var received = new List<LiveLogEntry>();

        using (var host = BuildLiveLogHost(contentRoot))
        {
            using var subscription = host.Services.GetRequiredService<ILiveLogFeed>().Subscribe(received.Add);
            WriteEachLevel(CreateLogger(host));
            var factory = host.Services.GetRequiredService<ILoggerFactory>();
            var microsoft = factory.CreateLogger("Microsoft.Library");
            var system = factory.CreateLogger("System.Net.Http");
            TestLog.Write(microsoft, LogLevel.Debug, "Microsoft debug");
            TestLog.Write(system, LogLevel.Debug, "System debug");
        }

        OwnFileTexts(contentRoot).ShouldBe(["Debug", "Information", "Warning", "Error"]);
        received.Select(entry => entry.Message).ShouldBe(["Information", "Warning", "Error"]);
        var texts = contentRoot.ReadJsonLogEntries()
            .Select(entry => entry.TryGetProperty("Text", out var text) ? text.GetString() : null)
            .ToArray();
        texts.ShouldNotContain("Microsoft debug");
        texts.ShouldNotContain("System debug");
    }

    [Fact]
    public void Log_LibraryDebugOverride_LetsLibraryDebugThroughAndOtherLibrariesKeepFloor()
    {
        using var contentRoot = new TemporaryContentRoot(
            """{ "Serilog": { "MinimumLevel": { "Default": "Warning", "Override": { "Microsoft.Library": "Debug" } } } }""");
        var received = new List<LiveLogEntry>();

        using (var host = BuildLiveLogHost(contentRoot))
        {
            using var subscription = host.Services.GetRequiredService<ILiveLogFeed>().Subscribe(received.Add);
            var factory = host.Services.GetRequiredService<ILoggerFactory>();
            var library = factory.CreateLogger("Microsoft.Library.Component");
            var other = factory.CreateLogger("Microsoft.Other");
            TestLog.Write(library, LogLevel.Debug, "Library debug");
            TestLog.Write(other, LogLevel.Information, "Other information");
        }

        var texts = contentRoot.ReadJsonLogEntries()
            .Select(entry => entry.TryGetProperty("Text", out var text) ? text.GetString() : null)
            .ToArray();
        texts.ShouldContain("Library debug");
        texts.ShouldContain("Other information");
        received.Select(entry => entry.Message).ShouldBe(["Library debug"]);
    }

    [Fact]
    public void Log_FileOff_WritesNoLogFile()
    {
        using var contentRoot = new TemporaryContentRoot(
            """{ "Serilog": { "Endpoints": { "File": { "Level": "Off" } } } }""");

        WriteEntry(contentRoot, WriteEachLevel);

        Directory.Exists(contentRoot.LogsPath).ShouldBeFalse();
    }

    [Fact]
    public void Log_EndpointOverrideAndGlobalOverride_EndpointOverrideWinsForFile()
    {
        using var contentRoot = new TemporaryContentRoot(
            $$"""
            {
              "Serilog": {
                "MinimumLevel": { "Default": "Information", "Override": { "{{_category}}": "Error" } },
                "Endpoints": { "File": { "Override": { "GitHubBackup.Infrastructure": "Debug" } } }
              }
            }
            """);

        WriteEntry(contentRoot, WriteEachLevel);

        OwnFileTexts(contentRoot).ShouldBe(["Debug", "Information", "Warning", "Error"]);
    }

    [Fact]
    public void Log_EndpointOverrideOff_DropsSourceFromFileOnly()
    {
        using var contentRoot = new TemporaryContentRoot(
            $$"""{ "Serilog": { "Endpoints": { "File": { "Override": { "{{_category}}": "Off" } } } } }""");
        var received = new List<LiveLogEntry>();

        using (var host = BuildLiveLogHost(contentRoot))
        {
            using var subscription = host.Services.GetRequiredService<ILiveLogFeed>().Subscribe(received.Add);
            WriteEachLevel(CreateLogger(host));
        }

        OwnFileTexts(contentRoot).ShouldBeEmpty();
        received.Select(entry => entry.Message).ShouldBe(["Information", "Warning", "Error"]);
    }

    [Fact]
    public void Log_LiveLogLevelError_FeedReceivesOnlyErrorsAndFileKeepsDefault()
    {
        using var contentRoot = new TemporaryContentRoot(
            """{ "Serilog": { "Endpoints": { "File": { "Level": null }, "LiveLog": { "Level": "Error" } } } }""");
        var received = new List<LiveLogEntry>();

        using (var host = BuildLiveLogHost(contentRoot))
        {
            using var subscription = host.Services.GetRequiredService<ILiveLogFeed>().Subscribe(received.Add);
            WriteEachLevel(CreateLogger(host));
        }

        OwnFileTexts(contentRoot).ShouldBe(["Information", "Warning", "Error"]);
        received.Select(entry => entry.Message).ShouldBe(["Error"]);
    }

    [Fact]
    public void Log_HostWithoutLiveLogBuffer_FeedReceivesNothing()
    {
        using var contentRoot = new TemporaryContentRoot(_informationAppSettings);
        var received = new List<LiveLogEntry>();

        using (var host = contentRoot.BuildHost())
        {
            using var subscription = host.Services.GetRequiredService<ILiveLogFeed>().Subscribe(received.Add);
            TestLog.Write(CreateLogger(host), LogLevel.Warning, "Warning");
        }

        received.ShouldBeEmpty();
        OwnFileTexts(contentRoot).ShouldBe(["Warning"]);
    }

    [Fact]
    public void Build_InvalidMinimumLevelAndEndpointSection_ThrowsOneExceptionWithBothFailures()
    {
        using var contentRoot = new TemporaryContentRoot(
            """{ "Serilog": { "MinimumLevel": { "Default": "Off" }, "Endpoints": { "Console": "Warning" } } }""");

        var exception = Should.Throw<OptionsValidationException>(() => contentRoot.BuildHost().Dispose());

        exception.Failures.ShouldBe(
        [
            "Serilog:MinimumLevel:Default must be one of Verbose, Debug, Information, Warning, Error, Fatal.",
            "Serilog:Endpoints:Console must be an object, not a value.",
        ]);
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
            using var exclusive = new FileStream(contentRoot.GetLogFilePath(extension), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
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
    /// Builds a host over <paramref name="contentRoot"/> with the live log feed attached, as the WPF application builds
    /// it.
    /// </summary>
    /// <param name="contentRoot">The content root of the host.</param>
    /// <param name="configurationArguments">The configuration keys, each in the form <c>--Section:Key=value</c>.</param>
    /// <returns>The host; the caller disposes it.</returns>
    private static IHost BuildLiveLogHost(TemporaryContentRoot contentRoot, params string[] configurationArguments)
    {
        var builder = GitHubBackupHostBuilder.Create(new()
        {
            ConfigurationArguments = configurationArguments,
            ContentRootPath = contentRoot.FolderPath,
            EnvironmentName = Environments.Production,
        });
        builder.Services.AddGitHubBackupLiveLogBuffer(100);
        return builder.Build();
    }

    /// <summary>
    /// Writes one entry per level from <see cref="LogLevel.Debug"/> to <see cref="LogLevel.Error"/>, each with the
    /// name of its level as text.
    /// </summary>
    /// <param name="logger">The logger.</param>
    private static void WriteEachLevel(ILogger logger)
    {
        foreach (var level in new[] { LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error })
        {
            var text = level.ToString();
            TestLog.Write(logger, level, text);
        }
    }

    /// <summary>
    /// Returns the texts of the structured log entries this test class wrote, without the entries of the host itself.
    /// </summary>
    /// <param name="contentRoot">The content root of the host.</param>
    /// <returns>The texts, in file order; empty when no entry reached the files, which then do not exist.</returns>
    private static string?[] OwnFileTexts(TemporaryContentRoot contentRoot) =>
        !Directory.Exists(contentRoot.LogsPath) ? [] : [.. contentRoot.ReadJsonLogEntries()
            .Where(entry => entry.TryGetProperty("SourceContext", out var source) && source.GetString() == _category)
            .Select(entry => entry.GetProperty("Text").GetString())];

    /// <summary>
    /// Creates the logger of this test class from the host.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>The logger.</returns>
    private static ILogger<SerilogFileLoggingTests> CreateLogger(IHost host) =>
        host.Services.GetRequiredService<ILogger<SerilogFileLoggingTests>>();
}
