using GitHubBackup.Core.Runs;
using Spectre.Console.Testing;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests <see cref="BackupSummaryRenderer"/> against a <see cref="TestConsole"/>.
/// </summary>
public sealed class BackupSummaryRendererTests : IDisposable
{
    private const string _logPath = @"C:\app\logs\githubbackup-20261111.log";

    private readonly TestConsole _output = TestCliConsole.CreateOutput();
    private readonly StringWriter _error = new();

    [Fact]
    public async Task RenderAsync_SeveralStatuses_WritesTableTotalsAndLogPath()
    {
        var result = new BackupRunResult(
            [
                new("octocat/hello", BackupItemStatus.Archived, "octocat_hello_20261111-120000_0123456789.zip", TimeSpan.FromSeconds(75.34), null),
                new("octocat/same", BackupItemStatus.UpToDate, null, TimeSpan.FromMilliseconds(420), null),
                new("octocat/broken", BackupItemStatus.Failed, null, TimeSpan.FromSeconds(3), "git exited with code 128."),
                new("octocat/gone", BackupItemStatus.Skipped, null, TimeSpan.Zero, "The repository is marked Unavailable."),
            ],
            [],
            []);

        await Render(result);

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            ┌────────────────┬──────────┬──────────────────────────────────────────────┬──────────┬───────────────────────────────────────┐
            │ Repository     │ Status   │ Archive                                      │ Duration │ Error                                 │
            ├────────────────┼──────────┼──────────────────────────────────────────────┼──────────┼───────────────────────────────────────┤
            │ octocat/hello  │ Archived │ octocat_hello_20261111-120000_0123456789.zip │ 1:15.3   │                                       │
            │ octocat/same   │ UpToDate │                                              │ 0:00.4   │                                       │
            │ octocat/broken │ Failed   │                                              │ 0:03.0   │ git exited with code 128.             │
            │ octocat/gone   │ Skipped  │                                              │ 0:00.0   │ The repository is marked Unavailable. │
            └────────────────┴──────────┴──────────────────────────────────────────────┴──────────┴───────────────────────────────────────┘
            Archived: 1, UpToDate: 1, Warning: 0, Failed: 1, Skipped: 1
            Log: C:\app\logs\githubbackup-20261111.log
            """));
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_BranchFallback_ShowsWarningWithArchiveAndMessage()
    {
        var result = new BackupRunResult(
            [new("octocat/hello", BackupItemStatus.Warning, "a.zip", TimeSpan.FromSeconds(1), "Branch 'dev' is missing.")],
            [],
            []);

        await Render(result);

        Normalize(_output.Output).ShouldContain("│ octocat/hello │ Warning │ a.zip   │ 0:01.0   │ Branch 'dev' is missing. │");
        Normalize(_output.Output).ShouldContain("Archived: 0, UpToDate: 0, Warning: 1, Failed: 0, Skipped: 0");
    }

    [Fact]
    public async Task RenderAsync_NoRepositories_WritesMessageTotalsAndLogPath()
    {
        await Render(new([], [], []));

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            No repositories to back up.
            Archived: 0, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 0
            Log: C:\app\logs\githubbackup-20261111.log
            """));
    }

    [Fact]
    public async Task RenderAsync_RunStopped_WritesErrorsAndLogPathToErrorOutput()
    {
        await Render(BackupRunResult.Stopped(["git was not found.", "Second error."], []));

        _error.ToString().ShouldBe(
            """
            Error: git was not found.
            Error: Second error.
            Log: C:\app\logs\githubbackup-20261111.log

            """.ReplaceLineEndings());
        _output.Output.ShouldBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_ConfigWarnings_WritesOneLineEachBeforeTable()
    {
        await Render(new([], ["sourcesRoot is inside backupRoot [x]."], []));

        Normalize(_output.Output).ShouldStartWith(Normalize(
            """
            Warning: sourcesRoot is inside backupRoot [x].
            No repositories to back up.
            """));
    }

    /// <summary>
    /// Disposes the outputs.
    /// </summary>
    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    /// <summary>
    /// Removes trailing spaces from each line and the final line break, and uses <c>\n</c> between lines, as in the
    /// expected raw strings.
    /// </summary>
    /// <param name="text">The console output.</param>
    /// <returns>The normalized text.</returns>
    private static string Normalize(string text) =>
        string.Join('\n', text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').Select(line => line.TrimEnd()));

    /// <summary>
    /// Renders <paramref name="result"/> with the log path of the tests.
    /// </summary>
    /// <param name="result">The result of the run.</param>
    /// <returns>A task that completes when the summary is written.</returns>
    private Task Render(BackupRunResult result) =>
        new BackupSummaryRenderer(new CliConsole(_output, _error, IsInputRedirected: false)).RenderAsync(result, _logPath);
}
