using System.Text.Json;
using GitHubBackup.Core;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Runs the <c>backup</c> command through the real host, with the GitHub API served by WireMock.Net on the loopback
/// interface, and reads the run it records in <c>data\history.db</c>.
/// </summary>
/// <remarks>
/// The scenarios need no clone: every repository is missing on GitHub or marked unavailable, so git and 7-Zip are only
/// located, never asked to clone or archive.
/// </remarks>
[Trait("Category", "EndToEnd")]
public sealed class BackupHistoryEndToEndTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly TemporaryContentRoot _contentRoot = new();
    private readonly TestConsole _output = TestCliConsole.CreateOutput();
    private readonly StringWriter _error = new();

    /// <summary>
    /// Serves an empty list of the token's repositories and a 404 for every repository lookup.
    /// </summary>
    public BackupHistoryEndToEndTests()
    {
        GivenCurrentUserRepositories();
        _server
            .Given(Request.Create().WithPath(path => path.StartsWith("/repos/", StringComparison.Ordinal)
                || path.StartsWith("/repositories/", StringComparison.Ordinal)).UsingGet())
            .AtPriority(10)
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "message": "Not Found", "documentation_url": "https://docs.github.com/rest" }"""));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Backup_SilentWithMissingRepository_RecordsSilentRunWithSkippedItemAndMissingEvent()
    {
        WriteBackupConfig(("owner", "gone", 1, "Active"));

        var exitCode = await RunAsync("backup", "--silent");

        exitCode.ShouldBe(ExitCode.DiscoveryEvents);
        var entry = await ReadRecordedRunAsync();
        entry.Mode.ShouldBe(RunMode.Silent);
        entry.IsDryRun.ShouldBeFalse();
        entry.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
        (entry.AccountFilter, entry.RepositoryFilter).ShouldBe((null, null));
        var item = entry.Items.ShouldHaveSingleItem();
        (item.Repository, item.Target, item.Status).ShouldBe(("owner/gone", BackupTarget.Repository, BackupItemStatus.Skipped));
        entry.DiscoveryEvents.ShouldBe([new("owner/gone", DiscoveryEventKind.Missing)]);
        entry.FinishedAt.ShouldBeGreaterThanOrEqualTo(entry.StartedAt);
    }

    [Fact]
    public async Task Backup_DryRun_RecordsDryRunWithEventsWithoutDecisions()
    {
        GivenCurrentUserRepositories(Repository(5, "other", "fresh"));
        WriteBackupConfig(("owner", "gone", 1, "Active"));

        var exitCode = await RunAsync("backup", "--dry-run");

        exitCode.ShouldBe(ExitCode.DiscoveryEvents);
        var entry = await ReadRecordedRunAsync();
        entry.IsDryRun.ShouldBeTrue();
        entry.Mode.ShouldBe(RunMode.Interactive);
        entry.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Skipped);
        entry.DiscoveryEvents.ShouldBe(
        [
            new("other/fresh", DiscoveryEventKind.New),
            new("owner/gone", DiscoveryEventKind.Missing),
        ]);
    }

    [Fact]
    public async Task Backup_SilentWithRepositoryFilter_RecordsFilter()
    {
        WriteBackupConfig(("owner", "off", 1, "Unavailable"), ("owner", "other", 2, "Unavailable"));

        var exitCode = await RunAsync("backup", "--silent", "--repo", "owner/off");

        exitCode.ShouldBe(ExitCode.Success);
        var entry = await ReadRecordedRunAsync();
        (entry.AccountFilter, entry.RepositoryFilter).ShouldBe((null, "owner/off"));
        entry.Items.ShouldHaveSingleItem().Repository.ShouldBe("owner/off");
        entry.DiscoveryEvents.ShouldBeEmpty();
    }

    /// <summary>
    /// Stops the server, disposes the outputs and deletes the content root.
    /// </summary>
    public void Dispose()
    {
        _server.Dispose();
        _output.Dispose();
        _error.Dispose();
        _contentRoot.Dispose();
    }

    /// <summary>
    /// Builds the JSON of one repository as GitHub returns it.
    /// </summary>
    /// <param name="id">The repository id.</param>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The JSON object.</returns>
    private static string Repository(long id, string owner, string name) =>
        $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "full_name": "{{owner}}/{{name}}",
          "owner": { "login": "{{owner}}", "id": 7, "type": "User" },
          "private": false,
          "fork": false,
          "archived": false,
          "has_wiki": false,
          "default_branch": "main",
          "html_url": "https://github.com/{{owner}}/{{name}}"
        }
        """;

    /// <summary>
    /// Serves the list of the token's repositories.
    /// </summary>
    /// <param name="repositories">The repository JSON objects.</param>
    private void GivenCurrentUserRepositories(params string[] repositories) =>
        _server
            .Given(Request.Create().WithPath("/user/repos").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"[{string.Join(",", repositories)}]"));

    /// <summary>
    /// Writes <c>backup-config.json</c> into the content root with standalone repositories and an archive root inside
    /// the content root.
    /// </summary>
    /// <param name="repositories">The owner, name, id and status of each repository.</param>
    private void WriteBackupConfig(params (string Owner, string Name, long Id, string Status)[] repositories)
    {
        var entries = repositories.Select(repository =>
            $$"""
            { "url": "https://github.com/{{repository.Owner}}/{{repository.Name}}", "id": {{repository.Id}}, "branch": null, "path": null, "status": "{{repository.Status}}" }
            """);
        var backupRoot = JsonSerializer.Serialize(Path.Combine(_contentRoot.FolderPath, "archives"));
        File.WriteAllText(
            Path.Combine(_contentRoot.FolderPath, "backup-config.json"),
            $$"""
            {
              "schemaVersion": 1,
              "backupRoot": {{backupRoot}},
              "sourcesRoot": null,
              "exclude": [],
              "accounts": [],
              "repositories": [ {{string.Join(",", entries)}} ]
            }
            """);
    }

    /// <summary>
    /// Returns the settings of the host: the token of <see cref="CliApplicationTests.ValidAppSettings"/> and the API
    /// address of the server.
    /// </summary>
    /// <returns>The JSON text of <c>appsettings.json</c>.</returns>
    private string AppSettings() =>
        CliApplicationTests.ValidAppSettings.Replace("https://api.github.com", _server.Url, StringComparison.Ordinal);

    /// <summary>
    /// Runs the application with <paramref name="args"/> over the content root.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code.</returns>
    private Task<ExitCode> RunAsync(params string[] args) =>
        new CliApplication(new CliConsole(_output, _error, false)).RunAsync(
            args,
            CliApplicationTests.CreateSettings(_contentRoot.FolderPath, AppSettings()),
            CancellationToken);

    /// <summary>
    /// Reads the run whose identifier the structured log carries from the run history of the content root.
    /// </summary>
    /// <returns>The recorded run.</returns>
    private async Task<RunHistoryEntry> ReadRecordedRunAsync()
    {
        var runId = _contentRoot.ReadJsonLogEntries()
            .Select(entry => entry.GetProperty(LogProperties.RunId).GetString())
            .Distinct()
            .ShouldHaveSingleItem();
        using var host = CliApplication.BuildHost(
            CliApplicationTests.CreateSettings(_contentRoot.FolderPath, AppSettings()), TestCliConsole.Create());
        var store = host.Services.GetRequiredService<IRunHistoryStore>();
        return (await store.GetAsync(Guid.Parse(runId!), CancellationToken)).ShouldNotBeNull();
    }
}
