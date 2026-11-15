using GitHubBackup.Core;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Testing;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests that the console host keeps the OTLP headers of the prefixed environment variable out of its output, its
/// error messages and its log files.
/// </summary>
[Collection(ProcessEnvironmentTestGroup.Name)]
public sealed class OtlpHeadersEnvironmentVariableTests : IDisposable
{
    /// <summary>The prefixed variable that sets <c>OpenTelemetry:Headers</c>.</summary>
    private const string _headersVariable = "GitHubBackup__OpenTelemetry__Headers";

    /// <summary>A header value without a recognisable format, masked only because the configuration sets it.</summary>
    private const string _headerValue = "header-value-without-format";

    /// <summary>The switch that enables the telemetry.</summary>
    private const string _enabledArgument = "--OpenTelemetry:Enabled=true";

    private readonly string? _previousValue = Environment.GetEnvironmentVariable(_headersVariable);
    private readonly TemporaryContentRoot _contentRoot = new();
    private readonly StringWriter _standardError = new();
    private readonly TestConsole _output = TestCliConsole.CreateOutput();

    [Fact]
    public async Task RunAsync_HeadersVariableTelemetryEnabled_NeverWritesValue()
    {
        Environment.SetEnvironmentVariable(_headersVariable, $"api-key={_headerValue}");

        var exitCode = await RunAsync("backup", _enabledArgument, "--OpenTelemetry:OtlpEndpoint=http://127.0.0.1:1");

        exitCode.ShouldBe(ExitCode.Critical);
        _standardError.ToString().ShouldNotContain(_headerValue);
        _output.Output.ShouldNotContain(_headerValue);
        var logFiles = Directory.GetFiles(Path.Combine(_contentRoot.FolderPath, LogFiles.FolderName));
        logFiles.ShouldNotBeEmpty();
        foreach (var logFile in logFiles)
        {
            ReadShared(logFile).ShouldNotContain(_headerValue);
        }
    }

    [Fact]
    public async Task RunAsync_HeadersVariableWithRemoteHttpEndpoint_ReturnsCriticalNamingKeyWithoutValue()
    {
        Environment.SetEnvironmentVariable(_headersVariable, $"api-key={_headerValue}");

        var exitCode = await RunAsync("backup", _enabledArgument, "--OpenTelemetry:OtlpEndpoint=http://collector.example:4317");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldContain(line => line.StartsWith(
            CliApplication.ConfigurationErrorPrefix + "OpenTelemetry:OtlpEndpoint must be an absolute https URI",
            StringComparison.Ordinal));
        _standardError.ToString().ShouldNotContain(_headerValue);
    }

    [Fact]
    public async Task RunAsync_MalformedHeadersVariable_ReturnsCriticalNamingKeyWithoutValue()
    {
        Environment.SetEnvironmentVariable(_headersVariable, _headerValue);

        var exitCode = await RunAsync("backup", _enabledArgument, "--OpenTelemetry:OtlpEndpoint=https://otlp.nr-data.net:4317");

        exitCode.ShouldBe(ExitCode.Critical);
        ErrorLines().ShouldContain(line => line.StartsWith(
            CliApplication.ConfigurationErrorPrefix + "OpenTelemetry:Headers must be a comma-separated list",
            StringComparison.Ordinal));
        _standardError.ToString().ShouldNotContain(_headerValue);
    }

    [Fact]
    public void BuildHost_HeadersVariable_MaskerMasksValue()
    {
        Environment.SetEnvironmentVariable(_headersVariable, $"api-key={_headerValue}");

        using var host = CliApplication.BuildHost(
            CliApplicationTests.CreateSettings(_contentRoot.FolderPath, CliApplicationTests.ValidAppSettings),
            TestCliConsole.Create());

        host.Services.GetRequiredService<ISecretMasker>().Mask($"Rejected {_headerValue}.").ShouldBe("Rejected ***.");
    }

    /// <summary>
    /// Restores the variable the tests change and deletes the content root.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_headersVariable, _previousValue);
        _standardError.Dispose();
        _output.Dispose();
        _contentRoot.Dispose();
    }

    /// <summary>
    /// Runs the application over <see cref="CliApplicationTests.ValidAppSettings"/> with the test outputs.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code.</returns>
    private Task<ExitCode> RunAsync(params string[] args) =>
        new CliApplication(new CliConsole(_output, _standardError, false)).RunAsync(
            args,
            CliApplicationTests.CreateSettings(_contentRoot.FolderPath, CliApplicationTests.ValidAppSettings),
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Returns the lines written to the standard error.
    /// </summary>
    /// <returns>The non-empty lines.</returns>
    private string[] ErrorLines() =>
        _standardError.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Reads a log file that a logger of the process may still hold open.
    /// </summary>
    /// <param name="path">The log file.</param>
    /// <returns>The content.</returns>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
