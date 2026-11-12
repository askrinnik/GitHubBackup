using System.Globalization;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.GitHub;
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
                new("octocat/hello.wiki", BackupItemStatus.Warning, "octocat_hello.wiki_20261111.zip", TimeSpan.FromSeconds(1), "Branch 'dev' is missing [x]."),
                new("octocat/same", BackupItemStatus.UpToDate, null, TimeSpan.FromMilliseconds(420), null),
                new("octocat/broken", BackupItemStatus.Failed, null, TimeSpan.FromSeconds(3), "git exited with code 128."),
                new("octocat/gone", BackupItemStatus.Skipped, null, TimeSpan.Zero, "The repository is marked Unavailable."),
            ],
            [],
            []);

        await Render(result);

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            ┌────────────────────┬──────────┬──────────────────────────────────────────────┬──────────┬───────────────────────────────────────┐
            │ Repository         │ Status   │ Archive                                      │ Duration │ Error                                 │
            ├────────────────────┼──────────┼──────────────────────────────────────────────┼──────────┼───────────────────────────────────────┤
            │ octocat/hello      │ Archived │ octocat_hello_20261111-120000_0123456789.zip │ 1:15.3   │                                       │
            │ octocat/hello.wiki │ Warning  │ octocat_hello.wiki_20261111.zip              │ 0:01.0   │ Branch 'dev' is missing [x].          │
            │ octocat/same       │ UpToDate │                                              │ 0:00.4   │                                       │
            │ octocat/broken     │ Failed   │                                              │ 0:03.0   │ git exited with code 128.             │
            │ octocat/gone       │ Skipped  │                                              │ 0:00.0   │ The repository is marked Unavailable. │
            └────────────────────┴──────────┴──────────────────────────────────────────────┴──────────┴───────────────────────────────────────┘
            Archived: 1, UpToDate: 1, Warning: 1, Failed: 1, Skipped: 1
            Log: C:\app\logs\githubbackup-20261111.log
            """));
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_DryRun_WritesHeaderTableDiscoveryAndTotals()
    {
        var result = new BackupRunResult(
            [
                new("octocat/hello", BackupItemStatus.WouldArchive, "octocat_hello_20261111-120000_0123456789.zip", TimeSpan.FromSeconds(2), null),
                new("octocat/hello.wiki", BackupItemStatus.WouldClone, null, TimeSpan.Zero, null),
                new("octocat/same", BackupItemStatus.UpToDate, null, TimeSpan.FromMilliseconds(420), null),
                new("octocat/branch", BackupItemStatus.Warning, "octocat_branch_20261111-120000_abcdef0123.zip", TimeSpan.FromSeconds(1), "Default branch 'main' is used."),
                new("octocat/broken", BackupItemStatus.Failed, null, TimeSpan.FromSeconds(3), "git exited with code 128."),
                new("octocat/gone", BackupItemStatus.Skipped, null, TimeSpan.Zero, "Missing on GitHub."),
            ],
            [],
            [])
        {
            IsDryRun = true,
            PlannedDiscovery = new(
                [new("octocat", Repository(7, "octocat", "fresh"))],
                [Repository(8, "other", "shared")],
                [new("octocat", "gone", 3, RepositoryStatus.Active, "octocat")],
                [new(new("octocat", "back", 4, RepositoryStatus.Unavailable, "octocat"), Repository(4, "octocat", "back"))],
                [new(new("octocat", "old", 5, RepositoryStatus.Active, "octocat"), Repository(5, "octocat", "new"))]),
        };

        await Render(result);

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            Dry run: nothing was cloned, updated, archived or saved.
            ┌────────────────────┬──────────────┬───────────────────────────────────────────────┬──────────┬────────────────────────────────┐
            │ Repository         │ Status       │ Archive                                       │ Duration │ Error                          │
            ├────────────────────┼──────────────┼───────────────────────────────────────────────┼──────────┼────────────────────────────────┤
            │ octocat/hello      │ WouldArchive │ octocat_hello_20261111-120000_0123456789.zip  │ 0:02.0   │                                │
            │ octocat/hello.wiki │ WouldClone   │                                               │ 0:00.0   │                                │
            │ octocat/same       │ UpToDate     │                                               │ 0:00.4   │                                │
            │ octocat/branch     │ Warning      │ octocat_branch_20261111-120000_abcdef0123.zip │ 0:01.0   │ Default branch 'main' is used. │
            │ octocat/broken     │ Failed       │                                               │ 0:03.0   │ git exited with code 128.      │
            │ octocat/gone       │ Skipped      │                                               │ 0:00.0   │ Missing on GitHub.             │
            └────────────────────┴──────────────┴───────────────────────────────────────────────┴──────────┴────────────────────────────────┘
            New: octocat/fresh
            New: other/shared
            Missing: octocat/gone
            Renamed: octocat/old -> octocat/new
            Returned: octocat/back
            WouldClone: 1, WouldArchive: 1, UpToDate: 1, Warning: 1, Failed: 1, Skipped: 1
            Log: C:\app\logs\githubbackup-20261111.log
            """));
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_DryRunNoRepositories_WritesHeaderMessageAndDryRunTotals()
    {
        await Render(new([], [], []) { IsDryRun = true });

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            Dry run: nothing was cloned, updated, archived or saved.
            No repositories to back up.
            WouldClone: 0, WouldArchive: 0, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 0
            Log: C:\app\logs\githubbackup-20261111.log
            """));
    }

    [Fact]
    public async Task RenderAsync_Cancelled_WritesTableCancelledLineAndTotalsWithCancelled()
    {
        var result = new BackupRunResult(
            [
                new("owner/repo", BackupItemStatus.Archived, "a.zip", TimeSpan.FromSeconds(2), null),
                new("owner/next", BackupItemStatus.Cancelled, null, TimeSpan.FromSeconds(1), "Cancelled."),
                new("owner/last", BackupItemStatus.Skipped, null, TimeSpan.Zero, "Not processed."),
            ],
            [],
            [])
        {
            IsCancelled = true,
        };

        await Render(result);

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            ┌────────────┬───────────┬─────────┬──────────┬────────────────┐
            │ Repository │ Status    │ Archive │ Duration │ Error          │
            ├────────────┼───────────┼─────────┼──────────┼────────────────┤
            │ owner/repo │ Archived  │ a.zip   │ 0:02.0   │                │
            │ owner/next │ Cancelled │         │ 0:01.0   │ Cancelled.     │
            │ owner/last │ Skipped   │         │ 0:00.0   │ Not processed. │
            └────────────┴───────────┴─────────┴──────────┴────────────────┘
            Cancelled: the run was stopped before it finished.
            Archived: 1, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 1, Cancelled: 1
            Log: C:\app\logs\githubbackup-20261111.log
            """));
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_CancelledWithoutItems_WritesCancelledLineAndTotals()
    {
        await Render(new([], [], []) { IsCancelled = true });

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            Cancelled: the run was stopped before it finished.
            Archived: 0, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 0, Cancelled: 0
            Log: C:\app\logs\githubbackup-20261111.log
            """));
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_DryRunCancelled_WritesHeaderCancelledLineAndDryRunTotalsWithCancelled()
    {
        await Render(new([], [], []) { IsDryRun = true, IsCancelled = true });

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            Dry run: nothing was cloned, updated, archived or saved.
            Cancelled: the run was stopped before it finished.
            WouldClone: 0, WouldArchive: 0, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 0, Cancelled: 0
            Log: C:\app\logs\githubbackup-20261111.log
            """));
    }

    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(999, "0:00.9")]
    [InlineData(125300, "2:05.3")]
    [InlineData(3660000, "61:00.0")]
    public async Task RenderAsync_Duration_FormatsMinutesSecondsAndTenths(long milliseconds, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            await Render(new(
                [new("octocat/hello", BackupItemStatus.UpToDate, null, TimeSpan.FromMilliseconds(milliseconds), null)],
                [],
                []));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Normalize(_output.Output).ShouldContain($"│ {expected} ");
    }

    [Fact]
    public async Task RenderAsync_RunStoppedWithConfigWarnings_WritesWarningsToOutputAndErrorsToErrorOutput()
    {
        await Render(BackupRunResult.Stopped(["git was not found."], ["First warning.", "Second warning."]));

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            Warning: First warning.
            Warning: Second warning.
            """));
        _error.ToString().ShouldBe(
            """
            Error: git was not found.
            Log: C:\app\logs\githubbackup-20261111.log

            """.ReplaceLineEndings());
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

    [Fact]
    public async Task RenderAsync_RunNotRecordedInHistory_WritesWarningAfterConfigWarningsAndFullSummary()
    {
        await Render(new([], ["First warning [x]."], []) { HistoryWarning = "Not recorded: 'C:\\app\\data\\history.db' [x]." });

        Normalize(_output.Output).ShouldBe(Normalize(
            """
            Warning: First warning [x].
            Warning: Not recorded: 'C:\app\data\history.db' [x].
            No repositories to back up.
            Archived: 0, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 0
            Log: C:\app\logs\githubbackup-20261111.log
            """));
    }

    [Fact]
    public async Task RenderAsync_StoppedRunNotRecordedInHistory_WritesWarningToOutputAndErrorsToErrorOutput()
    {
        await Render(BackupRunResult.Stopped([BackupOrchestrator.RunLockHeldMessage], []) with
        {
            HistoryWarning = "Not recorded.",
        });

        Normalize(_output.Output).ShouldBe("Warning: Not recorded.");
        _error.ToString().ShouldBe(
            """
            Error: A backup is already running.
            Log: C:\app\logs\githubbackup-20261111.log

            """.ReplaceLineEndings());
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
    /// Creates repository metadata as GitHub reports it.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="owner">The owner.</param>
    /// <param name="name">The name.</param>
    /// <returns>The repository.</returns>
    private static GitHubRepository Repository(long id, string owner, string name) =>
        new(id, owner, name, false, false, false, false, "main", $"https://github.com/{owner}/{name}");

    /// <summary>
    /// Renders <paramref name="result"/> with the log path of the tests.
    /// </summary>
    /// <param name="result">The result of the run.</param>
    /// <returns>A task that completes when the summary is written.</returns>
    private Task Render(BackupRunResult result) =>
        new BackupSummaryRenderer(new CliConsole(_output, _error, IsInputRedirected: false)).RenderAsync(result, _logPath);
}
