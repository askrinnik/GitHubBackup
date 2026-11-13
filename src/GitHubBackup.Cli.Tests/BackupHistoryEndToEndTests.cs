using GitHubBackup.Core;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Testing;

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
[Collection(BackupRunLockTestGroup.Name)]
public sealed class BackupHistoryEndToEndTests : IDisposable
{
    private readonly GitHubApiStub _api = new();
    private readonly TemporaryContentRoot _contentRoot = new();
    private readonly TestConsole _output = TestCliConsole.CreateOutput();
    private readonly StringWriter _error = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Backup_SilentWithMissingRepository_RecordsSilentRunWithSkippedItemAndMissingEvent()
    {
        WriteBackupConfig(config => config.WithRepository("owner", "gone", 1));

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
        _api.GivenCurrentUserRepositories(GitHubApiStub.Repository(5, "other", "fresh"));
        WriteBackupConfig(config => config.WithDiscoverAccessibleRepositories().WithRepository("owner", "gone", 1));

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
    public async Task Backup_DryRunStrict_DoesNotRecordRepositoryOfOtherOwnerAsNew()
    {
        _api.GivenCurrentUserRepositories(GitHubApiStub.Repository(5, "other", "fresh"));
        WriteBackupConfig(config => config.WithRepository("owner", "gone", 1));

        var exitCode = await RunAsync("backup", "--dry-run");

        exitCode.ShouldBe(ExitCode.DiscoveryEvents);
        var entry = await ReadRecordedRunAsync();
        entry.DiscoveryEvents.ShouldBe([new("owner/gone", DiscoveryEventKind.Missing)]);
    }

    [Fact]
    public async Task Backup_SilentWithRepositoryFilter_RecordsFilter()
    {
        WriteBackupConfig(config => config
            .WithRepository("owner", "off", 1, "Unavailable")
            .WithRepository("owner", "other", 2, "Unavailable"));

        var exitCode = await RunAsync("backup", "--silent", "--repo", "owner/off");

        exitCode.ShouldBe(ExitCode.Success);
        var entry = await ReadRecordedRunAsync();
        (entry.AccountFilter, entry.RepositoryFilter).ShouldBe((null, "owner/off"));
        entry.Items.ShouldHaveSingleItem().Repository.ShouldBe("owner/off");
        entry.DiscoveryEvents.ShouldBeEmpty();
    }

    /// <summary>
    /// Stops the API stub, disposes the outputs and deletes the content root.
    /// </summary>
    public void Dispose()
    {
        _api.Dispose();
        _output.Dispose();
        _error.Dispose();
        _contentRoot.Dispose();
    }

    /// <summary>
    /// Writes <c>backup-config.json</c> into the content root with an archive root inside the content root.
    /// </summary>
    /// <param name="configure">Adds the repositories of the scenario.</param>
    private void WriteBackupConfig(Func<BackupConfigJson, BackupConfigJson> configure) =>
        configure(new(Path.Combine(_contentRoot.FolderPath, "archives")))
            .WriteTo(Path.Combine(_contentRoot.FolderPath, "backup-config.json"));

    /// <summary>
    /// Returns the settings of the host: the token of <see cref="CliApplicationTests.ValidAppSettings"/> and the API
    /// address of the stub.
    /// </summary>
    /// <returns>The JSON text of <c>appsettings.json</c>.</returns>
    private string AppSettings() =>
        CliApplicationTests.ValidAppSettings.Replace("https://api.github.com", _api.Url, StringComparison.Ordinal);

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
