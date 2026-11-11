using System.Text.Json;
using GitHubBackup.Core;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Logging;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console.Testing;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests <see cref="CliApplication"/>: configuration sources, start-up validation, the error output and the log files.
/// </summary>
public sealed class CliApplicationTests : IDisposable
{
    /// <summary>A valid settings file with every section of the shipped <c>appsettings.json</c>.</summary>
    internal const string ValidAppSettings =
        """
        {
          "GitHub": { "ApiBaseUrl": "https://api.github.com", "Token": null },
          "Tools": { "GitPath": null, "SevenZipPath": "C:\\Program Files\\7-Zip\\7z.exe" },
          "Backup": { "ConfigPath": "backup-config.json", "ShortHashLength": 10, "VerifyArchive": true },
          "History": { "DatabasePath": "data\\history.db" },
          "OpenTelemetry": { "Enabled": false, "OtlpEndpoint": "http://localhost:4317" },
          "Serilog": { "MinimumLevel": { "Default": "Information" } }
        }
        """;

    /// <summary>A token value that must never reach the console or the log.</summary>
    internal const string Token = "ghp_TestTokenValue0123456789abcdefABCDEF";

    private readonly StringWriter _standardError = new();
    private readonly TestConsole _output = TestCliConsole.CreateOutput();
    private readonly TemporaryContentRoot _contentRoot = new();
    private bool _isInputRedirected;

    [Fact]
    public async Task RunAsync_BackupWithRedirectedInput_ReturnsCriticalWithHintBeforeReadingConfig()
    {
        _isInputRedirected = true;

        var exitCode = await RunAsync(ValidAppSettings, "backup");

        exitCode.ShouldBe(ExitCode.Critical);
        var lines = ErrorLines();
        lines.Length.ShouldBe(2);
        lines[0].ShouldBe(BackupSummaryRenderer.ErrorPrefix + BackupCommandHandler.RedirectedInputMessage);
        lines[1].ShouldStartWith(BackupSummaryRenderer.LogPrefix);
    }

    [Fact]
    public async Task RunAsync_BackupSilentWithRedirectedInput_DoesNotStopForRedirectedInput()
    {
        _isInputRedirected = true;

        var exitCode = await RunAsync(ValidAppSettings, "backup", "--silent");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines()[0].ShouldNotContain(BackupCommandHandler.RedirectedInputMessage);
    }

    [Fact]
    public async Task RunAsync_ValidConfiguration_ReturnsSuccessAndWritesNothing()
    {
        var exitCode = await RunAsync(ValidAppSettings);

        exitCode.ShouldBe(ExitCode.Success);
        _standardError.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_BackupWithoutBackupConfig_ReturnsCriticalWithErrorAndLogPath()
    {
        var exitCode = await RunAsync(ValidAppSettings, "backup");

        exitCode.ShouldBe(ExitCode.Critical);
        var lines = ErrorLines();
        lines.Length.ShouldBe(2);
        lines[0].ShouldStartWith(BackupSummaryRenderer.ErrorPrefix);
        lines[0].ShouldContain("backup-config.json");
        lines[1].ShouldStartWith(BackupSummaryRenderer.LogPrefix + Path.Combine(LogsPath, LogFiles.FileNamePrefix));
    }

    [Fact]
    public async Task RunAsync_BackupWithTokenKey_AcceptsKeyAndNeverWritesToken()
    {
        var exitCode = await RunAsync(ValidAppSettings, "backup", $"--GitHub:Token={Token}");

        exitCode.ShouldBe(ExitCode.Critical);
        _standardError.ToString().ShouldNotContain(CliApplication.CommandLineErrorPrefix);
        _standardError.ToString().ShouldNotContain(Token);
        _output.Output.ShouldNotContain(Token);
    }

    [Theory]
    [InlineData("Unrecognized option '--bogus'.", "backup", "--bogus")]
    [InlineData("Unrecognized option '--bogus'.", "--bogus=value")]
    [InlineData("Unrecognized command or argument 'bakup'.", "bakup")]
    [InlineData("Unrecognized option '--GitHub:Token'.", "backup", "--GitHub:Token")]
    public async Task RunAsync_UnknownArgument_ReturnsCriticalWithCommandLineError(string error, params string[] args)
    {
        var exitCode = await RunAsync(ValidAppSettings, args);

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe([CliApplication.CommandLineErrorPrefix + error]);
    }

    [Fact]
    public async Task RunAsync_LongUnknownArgument_NeverWritesIt()
    {
        var exitCode = await RunAsync(ValidAppSettings, "backup", Token);

        exitCode.ShouldBe(ExitCode.Critical);
        var line = ErrorLines().ShouldHaveSingleItem();
        line.ShouldStartWith(CliApplication.CommandLineErrorPrefix);
        line.ShouldNotContain(Token);
    }

    [Fact]
    public async Task RunAsync_HelpOption_WritesCommandsAndReturnsSuccess()
    {
        var exitCode = await RunAsync(ValidAppSettings, "--help");

        exitCode.ShouldBe(ExitCode.Success);
        _output.Output.ShouldContain("backup");
        _output.Output.ShouldContain("validate");
        _output.Output.ShouldContain("set-token");
        _standardError.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_BackupHelp_ListsAllOptions()
    {
        var exitCode = await RunAsync(ValidAppSettings, "backup", "--help");

        exitCode.ShouldBe(ExitCode.Success);
        foreach (var option in new[] { "--account", "--repo", "--dry-run", "--silent", "--config" })
        {
            _output.Output.ShouldContain(option);
        }
    }

    [Fact]
    public async Task RunAsync_ShortHashLengthOutOfRange_ReturnsCriticalWithOneLineNamingKey()
    {
        var exitCode = await RunAsync("""{ "Backup": { "ShortHashLength": 3 } }""");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe(["Configuration error: Backup:ShortHashLength must be between 4 and 40."]);
    }

    [Fact]
    public async Task RunAsync_ValueOfWrongType_ReturnsCriticalNamingKey()
    {
        var exitCode = await RunAsync("""{ "Backup": { "ShortHashLength": "ten" } }""");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe(["Configuration error: Backup:ShortHashLength cannot be converted to System.Int32."]);
        _standardError.ToString().ShouldNotContain("ten");
    }

    [Fact]
    public async Task RunAsync_TokenInTypedKey_NeverWritesToken()
    {
        var exitCode = await RunAsync(ValidAppSettings, $"--Backup:ShortHashLength={Token}");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe(["Configuration error: Backup:ShortHashLength cannot be converted to System.Int32."]);
        _standardError.ToString().ShouldNotContain(Token);
    }

    [Fact]
    public async Task RunAsync_TypeErrorsInSeveralSections_WritesOneLinePerKeyWithoutValues()
    {
        var exitCode = await RunAsync(
            """{ "Backup": { "ShortHashLength": "ten" }, "OpenTelemetry": { "Enabled": "perhaps" } }""");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().Order(StringComparer.Ordinal).ShouldBe(
        [
            "Configuration error: Backup:ShortHashLength cannot be converted to System.Int32.",
            "Configuration error: OpenTelemetry:Enabled cannot be converted to System.Boolean.",
        ]);
        _standardError.ToString().ShouldNotContain("perhaps");
    }

    [Fact]
    public async Task RunAsync_SeveralSectionsInvalid_WritesOneLinePerErrorWithoutStackTrace()
    {
        var exitCode = await RunAsync(
            """{ "GitHub": { "ApiBaseUrl": "api.github.com" }, "Tools": { "GitPath": "git.exe" }, "Backup": { "ShortHashLength": 41 } }""");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().Order(StringComparer.Ordinal).ShouldBe(
        [
            "Configuration error: Backup:ShortHashLength must be between 4 and 40.",
            "Configuration error: GitHub:ApiBaseUrl must be an absolute https URI, or http for a loopback address.",
            "Configuration error: Tools:GitPath must be an absolute path when set.",
        ]);
    }

    [Fact]
    public async Task RunAsync_BrokenJson_ReturnsCriticalWithParseError()
    {
        var exitCode = await RunAsync("""{ "Backup": { "ShortHashLength": 10, } """);

        exitCode.ShouldBe(ExitCode.Critical);
        var line = ErrorLines().ShouldHaveSingleItem();
        line.ShouldStartWith(CliApplication.ConfigurationErrorPrefix);
        line.ShouldContain("Could not parse the JSON file.");
    }

    [Fact]
    public async Task RunAsync_AppSettingsMissing_ReturnsCriticalNamingFile()
    {
        var exitCode = await RunAsync(new InMemoryFileProvider(new Dictionary<string, string>()));

        exitCode.ShouldBe(ExitCode.Critical);
        var line = ErrorLines().ShouldHaveSingleItem();
        line.ShouldStartWith(CliApplication.ConfigurationErrorPrefix);
        line.ShouldContain("appsettings.json");
    }

    [Fact]
    public async Task RunAsync_InvalidValueOnCommandLine_ReturnsCritical()
    {
        var exitCode = await RunAsync(ValidAppSettings, "--Backup:ShortHashLength=2");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe(["Configuration error: Backup:ShortHashLength must be between 4 and 40."]);
    }

    [Theory]
    [InlineData("--Serilog:MinimumLevel:Default=Loud", "Serilog:MinimumLevel:Default")]
    [InlineData("--Serilog:MinimumLevel:Override:Microsoft=Loud", "Serilog:MinimumLevel:Override:Microsoft")]
    public async Task RunAsync_InvalidLogLevel_ReturnsCriticalNamingKey(string argument, string key)
    {
        var exitCode = await RunAsync(ValidAppSettings, argument);

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe(
            [$"Configuration error: {key} must be one of Verbose, Debug, Information, Warning, Error, Fatal."]);
    }

    [Theory]
    [InlineData($$"""{ "GitHub": { "ApiBaseUrl": "x", "Token": "{{Token}}" }, "Backup": { "ShortHashLength": 1 } }""")]
    [InlineData($$"""{ "GitHub": { "Token": "{{Token}}" """)]
    [InlineData($$"""{ "GitHub": { "Token": "{{Token}} """)]
    public async Task RunAsync_InvalidConfigurationWithToken_NeverWritesToken(string appSettings)
    {
        var exitCode = await RunAsync(appSettings, $"--GitHub:Token={Token}");

        exitCode.ShouldBe(ExitCode.Critical);
        _standardError.ToString().ShouldNotContain(Token);
    }

    [Fact]
    public async Task RunAsync_ValidConfiguration_CreatesBothLogFilesOfCurrentDate()
    {
        var before = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);

        await RunAsync(ValidAppSettings);

        var after = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var fileNames = Directory.GetFiles(LogsPath).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        new[] { ExpectedLogFileNames(before), ExpectedLogFileNames(after) }.ShouldContain(expected => expected.SequenceEqual(fileNames));
    }

    [Fact]
    public async Task RunAsync_ValidConfiguration_WritesOneRunIdToEveryEntryOfBothFiles()
    {
        await RunAsync(ValidAppSettings);

        var runIds = ReadJsonEntries().Select(entry => entry.GetProperty(LogProperties.RunId).GetString()).Distinct().ToArray();
        var runId = runIds.ShouldHaveSingleItem().ShouldNotBeNull();
        Guid.TryParseExact(runId, "D", out _).ShouldBeTrue();
        var textLines = File.ReadAllLines(LogFilePath(LogFiles.TextExtension));
        textLines.ShouldNotBeEmpty();
        textLines.ShouldAllBe(line => line.Contains(runId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_TokenConfigured_NeverWritesTokenToLogFiles()
    {
        var exitCode = await RunAsync(ValidAppSettings, $"--GitHub:Token={Token}");

        exitCode.ShouldBe(ExitCode.Success);
        File.ReadAllText(LogFilePath(LogFiles.TextExtension)).ShouldNotContain(Token);
        File.ReadAllText(LogFilePath(LogFiles.JsonExtension)).ShouldNotContain(Token);
    }

    [Fact]
    public void BuildHost_CommandLineArgument_OverridesAppSettings()
    {
        using var host = CliApplication.BuildHost(CreateSettings(_contentRoot.FolderPath, ValidAppSettings, "--Backup:ShortHashLength=20"), TestCliConsole.Create());

        host.Services.GetRequiredService<IOptions<BackupOptions>>().Value.ShortHashLength.ShouldBe(20);
    }

    [Fact]
    public void BuildHost_RelativePaths_ResolveAgainstContentRoot()
    {
        using var host = CliApplication.BuildHost(CreateSettings(_contentRoot.FolderPath, ValidAppSettings), TestCliConsole.Create());

        host.Services.GetRequiredService<IOptions<BackupOptions>>().Value.ConfigPath
            .ShouldBe(Path.Combine(_contentRoot.FolderPath, "backup-config.json"));
        host.Services.GetRequiredService<IOptions<HistoryOptions>>().Value.DatabasePath
            .ShouldBe(Path.Combine(_contentRoot.FolderPath, "data", "history.db"));
    }

    [Fact]
    public void BuildHost_BackupConfigPath_OverridesConfigPath()
    {
        using var host = CliApplication.BuildHost(
            CreateSettings(_contentRoot.FolderPath, ValidAppSettings) with { BackupConfigPath = @"D:\configs\mine.json" },
            TestCliConsole.Create());

        host.Services.GetRequiredService<IOptions<BackupOptions>>().Value.ConfigPath.ShouldBe(@"D:\configs\mine.json");
    }

    [Fact]
    public void BuildHost_BackupConfigPathAndConfigPathKey_BackupConfigPathWins()
    {
        using var host = CliApplication.BuildHost(
            CreateSettings(_contentRoot.FolderPath, ValidAppSettings, @"--Backup:ConfigPath=D:\other.json") with { BackupConfigPath = @"D:\mine.json" },
            TestCliConsole.Create());

        host.Services.GetRequiredService<IOptions<BackupOptions>>().Value.ConfigPath.ShouldBe(@"D:\mine.json");
    }

    [Theory]
    [InlineData("backup", "--config")]
    [InlineData("backup", "--config=")]
    [InlineData("backup", "--config", " ")]
    [InlineData("backup", "--config", "--silent")]
    [InlineData("validate", "--config", "--silent")]
    [InlineData("backup", "--config", @"D:\mine.json", "--config", @"D:\other.json")]
    public async Task RunAsync_InvalidConfigOption_ReturnsCriticalWithOneCommandLineErrorAndValueNotShown(params string[] args)
    {
        var exitCode = await RunAsync(ValidAppSettings, args);

        exitCode.ShouldBe(ExitCode.Critical);
        var line = ErrorLines().ShouldHaveSingleItem();
        line.ShouldStartWith(CliApplication.CommandLineErrorPrefix);
        line.ShouldNotContain("mine.json");
        line.ShouldNotContain("other.json");
    }

    [Fact]
    public async Task RunAsync_ConfigOptionWithoutCommand_ReturnsCriticalWithCommandLineError()
    {
        var exitCode = await RunAsync(ValidAppSettings, "--config");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe([CliApplication.CommandLineErrorPrefix + "Unrecognized option '--config'."]);
    }

    [Fact]
    public async Task RunAsync_ConfigOptionNotAPath_ReturnsCriticalWithOneLine()
    {
        var exitCode = await RunAsync(ValidAppSettings, "backup", "--config", "D:\\mine\0.json");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe([CliApplication.CommandLineErrorPrefix + CliCommandLine.ConfigInvalidMessage]);
    }

    [Theory]
    [InlineData("--account", "octocat", "--repo", "octocat/hello")]
    [InlineData("--repo", "octocat/hello", "--account", "octocat")]
    public async Task RunAsync_AccountAndRepo_ReturnsCriticalWithOneLine(params string[] options)
    {
        var exitCode = await RunAsync(ValidAppSettings, ["backup", .. options]);

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe([CliApplication.CommandLineErrorPrefix + CliCommandLine.AccountAndRepositoryMessage]);
    }

    [Fact]
    public async Task RunAsync_CommandLineError_DoesNotBuildHost()
    {
        var exitCode = await RunAsync(new InMemoryFileProvider(new Dictionary<string, string>()), "backup", "--bogus");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe([CliApplication.CommandLineErrorPrefix + "Unrecognized option '--bogus'."]);
    }

    [Fact]
    public async Task RunAsync_ConfigOption_ReadsThatFile()
    {
        var path = Path.Combine(_contentRoot.FolderPath, "mine.json");

        var exitCode = await RunAsync(ValidAppSettings, "backup", "--config", path);

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines()[0].ShouldContain(path);
    }

    [Fact]
    public async Task RunAsync_ValidateWithoutBackupConfig_ReturnsCriticalWithErrorAndLogPath()
    {
        var exitCode = await RunAsync(ValidAppSettings, "validate");

        exitCode.ShouldBe(ExitCode.Critical);
        var lines = ErrorLines();
        lines[0].ShouldStartWith(BackupSummaryRenderer.ErrorPrefix);
        lines[0].ShouldContain("backup-config.json");
        lines[^1].ShouldStartWith(BackupSummaryRenderer.LogPrefix);
    }

    [Fact]
    public async Task RunAsync_SetToken_ReturnsCriticalNotImplemented()
    {
        var exitCode = await RunAsync(ValidAppSettings, "set-token");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe([SetTokenCommandHandler.NotImplementedMessage]);
    }

    [Fact]
    public async Task RunAsync_SetTokenWithArgument_NeverWritesIt()
    {
        var exitCode = await RunAsync(ValidAppSettings, "set-token", Token);

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldBe([CliApplication.CommandLineErrorPrefix + CliCommandLine.SetTokenArgumentMessage]);
    }

    [Fact]
    public async Task RunAsync_BackupDryRun_ReturnsCriticalAndChangesNothing()
    {
        var exitCode = await RunAsync(ValidAppSettings, "backup", "--dry-run");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines()[0].ShouldBe(BackupSummaryRenderer.ErrorPrefix + BackupCommandHandler.DryRunNotImplementedMessage);
    }

    [Fact]
    public void BuildHost_ProductionEnvironment_UsesSettingsEnvironment()
    {
        using var host = CliApplication.BuildHost(CreateSettings(_contentRoot.FolderPath, ValidAppSettings), TestCliConsole.Create());

        host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName.ShouldBe(Environments.Production);
    }

    /// <summary>
    /// Disposes the outputs and deletes the content root.
    /// </summary>
    public void Dispose()
    {
        _standardError.Dispose();
        _output.Dispose();
        _contentRoot.Dispose();
    }

    /// <summary>
    /// Creates host settings that read <paramref name="appSettings"/> as <c>appsettings.json</c>.
    /// </summary>
    /// <param name="contentRootPath">The existing folder that the host uses as its content root.</param>
    /// <param name="appSettings">The JSON text of the settings file.</param>
    /// <param name="configurationArguments">The configuration keys, each in the form <c>--Section:Key=value</c>.</param>
    /// <returns>The host settings.</returns>
    internal static GitHubBackupHostSettings CreateSettings(
        string contentRootPath, string appSettings, params string[] configurationArguments) =>
        CreateSettings(contentRootPath, InMemoryFileProvider.WithAppSettings(appSettings), configurationArguments);

    /// <summary>
    /// Creates host settings that read <c>appsettings.json</c> from <paramref name="fileProvider"/>.
    /// </summary>
    /// <param name="contentRootPath">The existing folder that the host uses as its content root.</param>
    /// <param name="fileProvider">The provider of the settings file.</param>
    /// <param name="configurationArguments">The configuration keys, each in the form <c>--Section:Key=value</c>.</param>
    /// <returns>The host settings.</returns>
    private static GitHubBackupHostSettings CreateSettings(
        string contentRootPath, InMemoryFileProvider fileProvider, string[] configurationArguments) => new()
        {
            ConfigurationArguments = configurationArguments,
            ContentRootPath = contentRootPath,
            EnvironmentName = Environments.Production,
            ConfigurationFileProvider = fileProvider,
        };

    /// <summary>
    /// Returns the two log file names expected for <paramref name="date"/>, in ordinal order.
    /// </summary>
    /// <param name="date">The local date.</param>
    /// <returns>The JSON and the text file names.</returns>
    private static string[] ExpectedLogFileNames(DateOnly date) =>
        [LogFiles.GetFileName(date, LogFiles.JsonExtension), LogFiles.GetFileName(date, LogFiles.TextExtension)];

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

    /// <summary>Gets the log folder of the content root.</summary>
    private string LogsPath => Path.Combine(_contentRoot.FolderPath, LogFiles.FolderName);

    /// <summary>
    /// Runs the application over <paramref name="appSettings"/> and <paramref name="args"/>.
    /// </summary>
    /// <param name="appSettings">The JSON text of the settings file.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code.</returns>
    private Task<ExitCode> RunAsync(string appSettings, params string[] args) =>
        RunAsync(InMemoryFileProvider.WithAppSettings(appSettings), args);

    /// <summary>
    /// Runs the application with <c>appsettings.json</c> served by <paramref name="fileProvider"/>.
    /// </summary>
    /// <param name="fileProvider">The provider of the settings file.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code.</returns>
    private Task<ExitCode> RunAsync(InMemoryFileProvider fileProvider, params string[] args) =>
        new CliApplication(new CliConsole(_output, _standardError, _isInputRedirected)).RunAsync(
            args,
            CreateSettings(_contentRoot.FolderPath, fileProvider, []),
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Returns the path of the only log file with <paramref name="extension"/>.
    /// </summary>
    /// <param name="extension">The extension of the file.</param>
    /// <returns>The path.</returns>
    private string LogFilePath(string extension) =>
        Directory.GetFiles(LogsPath, LogFiles.FileNamePrefix + "*" + extension).ShouldHaveSingleItem();

    /// <summary>
    /// Reads the entries of the structured log.
    /// </summary>
    /// <returns>One element per entry.</returns>
    private JsonElement[] ReadJsonEntries() =>
        [.. File.ReadAllLines(LogFilePath(LogFiles.JsonExtension)).Select(ParseEntry)];

    /// <summary>
    /// Returns the lines written to the error writer.
    /// </summary>
    /// <returns>The non-empty lines.</returns>
    private string[] ErrorLines() =>
        _standardError.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
