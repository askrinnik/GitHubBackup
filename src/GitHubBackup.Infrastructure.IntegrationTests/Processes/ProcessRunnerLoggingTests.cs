using System.ComponentModel;
using System.Globalization;
using System.IO.Abstractions;
using System.Text.Json;
using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Infrastructure.IntegrationTests.Processes;

/// <summary>
/// Tests the log entries of <see cref="ProcessRunner"/> for real <c>cmd.exe</c> processes, both as the logger receives
/// them and as the log files of the shared host store them.
/// </summary>
public sealed class ProcessRunnerLoggingTests : IDisposable
{
    /// <summary>A settings file that writes entries from the information level.</summary>
    private const string _informationAppSettings = """{ "Serilog": { "MinimumLevel": { "Default": "Information" } } }""";

    /// <summary>A secret without a recognisable format, so only its registration can mask it.</summary>
    private const string _secretValue = "opaque-secret-value-0123";

    /// <summary>A value that is not secret but must not reach the log either.</summary>
    private const string _plainValue = "plain-value-4567";

    private const string _secretName = "GITHUBBACKUP_TEST_SECRET";
    private const string _plainName = "GITHUBBACKUP_TEST_PLAIN";

    private const int _startingEventId = 1;
    private const int _outputLineEventId = 2;
    private const int _exitedEventId = 3;
    private const int _cancelledEventId = 4;
    private const int _killFailedEventId = 5;

    private readonly TemporaryContentRoot _contentRoot = new(_informationAppSettings);
    private readonly FakeLogger<ProcessRunner> _logger = new();
    private readonly ProcessRunner _runner;

    /// <summary>
    /// Creates a runner that writes to <see cref="_logger"/>.
    /// </summary>
    public ProcessRunnerLoggingTests() =>
        _runner = new(new SecretMasker(), new FileSystem(), TimeProvider.System, _logger);

    [Fact]
    public async Task RunAsync_SecretPrintedByProcess_IsMaskedInBothLogFiles()
    {
        using (var host = _contentRoot.BuildHost())
        {
            await host.Services.GetRequiredService<IProcessRunner>()
                .RunAsync(CreateSecretRequest(), TestContext.Current.CancellationToken);
        }

        foreach (var content in new[]
        {
            _contentRoot.ReadTextLog(), File.ReadAllText(_contentRoot.GetLogFilePath(LogFiles.JsonExtension)),
        })
        {
            content.ShouldNotContain(_secretValue);
            content.ShouldNotContain(_plainValue);
            content.ShouldContain("out ***");
            content.ShouldContain("err ***");
            content.ShouldContain($"{_secretName}, {_plainName}");
        }
    }

    [Fact]
    public async Task RunAsync_SecretPrintedByProcess_IsMaskedBeforeLogger()
    {
        var result = await _runner.RunAsync(CreateSecretRequest(), TestContext.Current.CancellationToken);

        var records = _logger.Collector.GetSnapshot();
        records.ShouldNotBeEmpty();
        foreach (var record in records)
        {
            record.Message.ShouldNotContain(_secretValue);
            record.Message.ShouldNotContain(_plainValue);
            foreach (var pair in record.StructuredState ?? [])
            {
                var text = pair.Value ?? string.Empty;
                text.ShouldNotContain(_secretValue);
                text.ShouldNotContain(_plainValue);
            }
        }

        records.ShouldContain(record => record.Id.Id == _outputLineEventId && record.GetStructuredStateValue("Line") == "out ***");
        records.ShouldContain(record => record.Id.Id == _outputLineEventId && record.GetStructuredStateValue("Line") == "err ***");
        result.StandardOutput.ShouldBe([$"out {_secretValue}"]);
    }

    [Fact]
    public async Task RunAsync_InsideRepositoryScope_OutputEntriesCarryRepositoryToolAndStream()
    {
        var request = TestScript.Create(_contentRoot.FolderPath, ["echo to output", ">&2 echo to error"]);
        using (var host = _contentRoot.BuildHost())
        {
            var logger = host.Services.GetRequiredService<ILogger<ProcessRunnerLoggingTests>>();
            using var scope = logger.BeginRepositoryScope("octocat/hello-world");
            await host.Services.GetRequiredService<IProcessRunner>().RunAsync(request, TestContext.Current.CancellationToken);
        }

        var lines = _contentRoot.ReadJsonLogEntries()
            .Where(entry => entry.TryGetProperty(LogProperties.Stream, out _))
            .ToArray();
        lines.Select(entry => (Stream: GetString(entry, LogProperties.Stream), Line: GetString(entry, "Line")))
            .OrderBy(line => line.Stream, StringComparer.Ordinal)
            .ShouldBe([("stderr", "to error"), ("stdout", "to output")]);
        lines.ShouldAllBe(entry => GetString(entry, LogProperties.Tool) == "cmd");
        lines.ShouldAllBe(entry => GetString(entry, LogProperties.Repository) == "octocat/hello-world");
    }

    [Fact]
    public async Task RunAsync_Output_LogsStartEachLineAndExitInOrder()
    {
        var request = TestScript.Create(_contentRoot.FolderPath, ["echo first", "echo.", "echo second"]);

        await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        var records = _logger.Collector.GetSnapshot();
        records.Select(record => record.Id.Id).ShouldBe([_startingEventId, _outputLineEventId, _outputLineEventId, _exitedEventId]);
        var lines = records.Where(record => record.Id.Id == _outputLineEventId).ToList();
        lines.Select(record => record.GetStructuredStateValue("Line")).ShouldBe(["first", "second"]);
        lines.ShouldAllBe(record => record.GetStructuredStateValue(LogProperties.Stream) == "stdout");
        records.ShouldAllBe(record => record.GetStructuredStateValue(LogProperties.Tool) == "cmd");
        var exited = records[^1];
        exited.Level.ShouldBe(LogLevel.Information);
        exited.GetStructuredStateValue("ExitCode").ShouldBe("0");
        TimeSpan.Parse(exited.GetStructuredStateValue("Duration")!, CultureInfo.InvariantCulture).ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_LogsExitAsWarning()
    {
        var request = TestScript.Create(_contentRoot.FolderPath, ["exit /b 5"]);

        await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        var exited = _logger.LatestRecord;
        exited.Id.Id.ShouldBe(_exitedEventId);
        exited.Level.ShouldBe(LogLevel.Warning);
        exited.GetStructuredStateValue("ExitCode").ShouldBe("5");
    }

    [Fact]
    public async Task RunAsync_StandardOutputSecret_ReturnsOutputButLogsOnlyStartErrorAndExit()
    {
        var request = TestScript.Create(_contentRoot.FolderPath, [$"echo {_secretValue}", ">&2 echo to error"]) with
        {
            IsStandardOutputSecret = true,
        };

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.StandardOutput.ShouldBe([_secretValue]);
        var records = _logger.Collector.GetSnapshot();
        records.Select(record => record.Id.Id).ShouldBe([_startingEventId, _outputLineEventId, _exitedEventId]);
        var line = records[1];
        line.GetStructuredStateValue(LogProperties.Stream).ShouldBe("stderr");
        line.GetStructuredStateValue("Line").ShouldBe("to error");
        foreach (var record in records)
        {
            record.Message.ShouldNotContain(_secretValue);
            record.StructuredState.ShouldNotBeNull()
                .ShouldAllBe(pair => pair.Value == null || !pair.Value.Contains(_secretValue));
        }
    }

    [Fact]
    public async Task RunAsync_NoOutput_LogsNoLineEntries()
    {
        var request = TestScript.Create(_contentRoot.FolderPath, ["exit /b 0"]);

        await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        EventIds().ShouldBe([_startingEventId, _exitedEventId]);
    }

    [Fact]
    public async Task RunAsync_Cancelled_LogsCancellationLast()
    {
        var request = TestScript.Create(_contentRoot.FolderPath, ["ping -n 60 127.0.0.1 >nul"]);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Should.ThrowAsync<OperationCanceledException>(() => _runner.RunAsync(request, cancellation.Token));

        EventIds().ShouldBe([_startingEventId, _cancelledEventId]);
    }

    [Fact]
    public async Task RunAsync_KillFails_LogsMaskedFailureAndThrowsCancellation()
    {
        // The replacement kills the tree so no process outlives the test, then reports a failure whose message
        // carries the secret of the run.
        var runner = new ProcessRunner(new SecretMasker(), new FileSystem(), TimeProvider.System, _logger)
        {
            KillProcessTree = process =>
            {
                process.Kill(entireProcessTree: true);
                throw new Win32Exception(5, $"Access is denied for {_secretValue}");
            },
        };
        var request = TestScript.Create(_contentRoot.FolderPath, ["ping -n 60 127.0.0.1 >nul"]) with
        {
            EnvironmentVariables = [new(_secretName, _secretValue, IsSecret: true)],
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Should.ThrowAsync<OperationCanceledException>(() => runner.RunAsync(request, cancellation.Token));

        EventIds().ShouldBe([_startingEventId, _killFailedEventId, _cancelledEventId]);
        _logger.Collector.GetSnapshot()[1].GetStructuredStateValue("Error").ShouldBe("Access is denied for ***");
    }

    /// <summary>
    /// Deletes the temporary content root.
    /// </summary>
    public void Dispose() => _contentRoot.Dispose();

    /// <summary>
    /// Returns the event ids of the records the runner logged, in order.
    /// </summary>
    /// <returns>The event ids.</returns>
    private int[] EventIds() => [.. _logger.Collector.GetSnapshot().Select(record => record.Id.Id)];

    /// <summary>
    /// Creates a request whose script prints the secret variable to both streams and never prints the plain one.
    /// </summary>
    /// <returns>The request.</returns>
    private ProcessRequest CreateSecretRequest() =>
        TestScript.Create(_contentRoot.FolderPath, [$"echo out %{_secretName}%", $">&2 echo err %{_secretName}%"]) with
        {
            EnvironmentVariables = [new(_secretName, _secretValue, IsSecret: true), new(_plainName, _plainValue)],
        };

    /// <summary>
    /// Returns the string value of the property <paramref name="name"/> of a structured log entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or <see langword="null"/> when the entry lacks the property.</returns>
    private static string? GetString(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) ? value.GetString() : null;
}
