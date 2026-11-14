using System.Text.Json.Nodes;
using GitHubBackup.Core;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Runs;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Runs the built console executable as a separate process against local bare repositories and a stubbed GitHub API,
/// and checks its exit code, its output and the files it leaves on disk.
/// </summary>
/// <remarks>
/// git and 7-Zip run for real. The executable keeps cloning <c>https://github.com/&lt;owner&gt;/&lt;name&gt;.git</c>;
/// the global git configuration of the process redirects that address to the bare repository of the sandbox.
/// </remarks>
[Trait("Category", "EndToEnd")]
[Collection(BackupRunLockTestGroup.Name)]
public sealed class CliEndToEndTests : IDisposable
{
    private const string _owner = "owner";
    private const string _account = "acme";
    private const string _missingSevenZipArgument = @"--Tools:SevenZipPath=C:\GitHubBackup.Tests.Missing\7z.exe";

    private readonly LocalGitSandbox _sandbox = new();
    private readonly GitHubApiStub _api = new();
    private readonly CliInstallation _installation;

    /// <summary>The JSON objects of the repositories the API lists for the token.</summary>
    private readonly List<string> _currentUserRepositories = [];

    /// <summary>The configuration of the scenario, written again whenever a repository is tracked.</summary>
    private readonly BackupConfigJson _config;

    /// <summary>
    /// Installs the executable for the API stub and the remotes of the sandbox.
    /// </summary>
    public CliEndToEndTests()
    {
        _installation = new(_api.Url, Path.Combine(_sandbox.RootPath, "remotes"));
        _config = _installation.NewBackupConfig();
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Backup_SilentTrackedRepository_ExitsSuccessWithCloneArchiveAndState()
    {
        await GivenTrackedRepositoriesAsync("a");

        var result = await _installation.RunAsync("backup", "--silent");

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        result.StandardError.ShouldBeEmpty();
        result.StandardOutput.ShouldContain(nameof(BackupItemStatus.Archived));
        _installation.FindArchives(_owner, "a").ShouldHaveSingleItem();
        _installation.FindStateFiles().ShouldHaveSingleItem();
        var gitConfig = await File.ReadAllTextAsync(
            Path.Combine(_installation.CloneFolder(_owner, "a"), ".git", "config"), CancellationToken);
        gitConfig.ShouldContain($"https://github.com/{_owner}/a");
        result.StandardOutput.ShouldNotContain(CliApplicationTests.HostTestToken);
        result.StandardError.ShouldNotContain(CliApplicationTests.HostTestToken);
        gitConfig.ShouldNotContain(CliApplicationTests.HostTestToken);
    }

    [Fact]
    public async Task Backup_SilentOneRemoteMissing_ExitsWithFailuresAndArchivesTheOther()
    {
        await GivenTrackedRepositoriesAsync("a");
        GivenTrackedRepositoryWithoutRemote("b", 2);

        var result = await _installation.RunAsync("backup", "--silent");

        result.ExitCode.ShouldBe((int)ExitCode.RepositoriesFailed, result.ToString());
        _installation.FindArchives(_owner, "a").ShouldHaveSingleItem();
        _installation.FindArchives(_owner, "b").ShouldBeEmpty();
    }

    [Fact]
    public async Task Backup_SilentNewRepositoryWithOpenInput_ExitsWithDiscoveryEventsAndAddsIt()
    {
        _config.WithDiscoverAccessibleRepositories();
        await GivenTrackedRepositoriesAsync("a");
        await GivenNewRepositoryAsync("new", 3);

        var result = await _installation.RunAsync("backup", "--silent");

        result.ExitCode.ShouldBe((int)ExitCode.DiscoveryEvents, result.ToString());
        (await ReadBackupConfigAsync()).ShouldContain($"https://github.com/{_owner}/new");
        _installation.FindArchives(_owner, "new").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Backup_SilentAccessibleRepositoryOfOtherOwner_AddsItToRepositoriesAndExitsWithDiscoveryEvents()
    {
        _config.WithDiscoverAccessibleRepositories();
        await GivenTrackedRepositoriesAsync("a");
        await GivenOtherOwnerRepositoryAsync("lib", 7);

        var result = await _installation.RunAsync("backup", "--silent");

        result.ExitCode.ShouldBe((int)ExitCode.DiscoveryEvents, result.ToString());
        (await ReadBackupConfigAsync()).ShouldContain("https://github.com/other/lib");
        _installation.FindArchives("other", "lib").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Backup_SilentStrictRepositoryOfOtherOwner_ExitsSuccessAndLeavesConfigUnchanged()
    {
        await GivenTrackedRepositoriesAsync("a");
        await GivenOtherOwnerRepositoryAsync("lib", 7);
        var before = await File.ReadAllBytesAsync(_installation.BackupConfigPath, CancellationToken);

        var result = await _installation.RunAsync("backup", "--silent");

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        (await File.ReadAllBytesAsync(_installation.BackupConfigPath, CancellationToken)).ShouldBe(before);
        _installation.FindArchives("other", "lib").ShouldBeEmpty();
        _installation.FindArchives(_owner, "a").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Backup_InteractiveWithRedirectedInput_ExitsCriticalWithHintAndTouchesNothing()
    {
        await GivenTrackedRepositoriesAsync("a");
        var config = await ReadBackupConfigAsync();

        var result = await _installation.RunAsync("backup");

        result.ExitCode.ShouldBe((int)ExitCode.Critical, result.ToString());
        result.ErrorLines()[0].ShouldBe(BackupSummaryRenderer.ErrorPrefix + BackupCommandHandler.RedirectedInputMessage);
        Directory.Exists(_installation.SourcesPath).ShouldBeFalse();
        _installation.FindArchives().ShouldBeEmpty();
        (await ReadBackupConfigAsync()).ShouldBe(config);
    }

    [Theory]
    [InlineData("--repo", "backup", "--silent", "--account", _account, "--repo", _owner + "/a")]
    [InlineData("--repo", "backup", "--silent", "--repo", "not-a-repo")]
    [InlineData("7-Zip", "backup", "--silent", _missingSevenZipArgument)]
    public async Task Backup_CriticalArguments_ExitsCriticalWithErrorAndNoArchive(
        string expectedError, params string[] arguments)
    {
        await GivenTrackedRepositoriesAsync("a");

        var result = await _installation.RunAsync(arguments);

        result.ExitCode.ShouldBe((int)ExitCode.Critical, result.ToString());
        result.ErrorLines()[0].ShouldContain(expectedError, Case.Sensitive, result.ToString());
        _installation.FindArchives().ShouldBeEmpty();
    }

    [Fact]
    public async Task Backup_InvalidBackupConfig_ExitsCriticalWithErrorAndLogPath()
    {
        await File.WriteAllTextAsync(_installation.BackupConfigPath, """{ "schemaVersion": 1, """, CancellationToken);

        var result = await _installation.RunAsync("backup", "--silent");

        result.ExitCode.ShouldBe((int)ExitCode.Critical, result.ToString());
        var lines = result.ErrorLines();
        lines.Length.ShouldBe(2, result.ToString());
        lines[0].ShouldStartWith(BackupSummaryRenderer.ErrorPrefix);
        lines[0].ShouldContain("backup-config.json");
        lines[1].ShouldStartWith(
            BackupSummaryRenderer.LogPrefix + Path.Combine(_installation.LogsPath, LogFiles.FileNamePrefix));
    }

    [Fact]
    public async Task Backup_DryRunBeforeFirstBackup_ChangesNothingOnDisk()
    {
        _config.WithDiscoverAccessibleRepositories();
        await GivenTrackedRepositoriesAsync("a");
        await GivenNewRepositoryAsync("new", 3);
        var config = await File.ReadAllBytesAsync(_installation.BackupConfigPath, CancellationToken);

        var result = await _installation.RunAsync("backup", "--dry-run");

        result.ExitCode.ShouldBe((int)ExitCode.DiscoveryEvents, result.ToString());
        result.StandardOutput.ShouldContain(nameof(BackupItemStatus.WouldClone));
        Directory.Exists(_installation.CloneFolder(_owner, "a")).ShouldBeFalse();
        Directory.Exists(Path.Combine(_installation.SourcesPath, RepositoryPathResolver.TemporaryArchiveFolderName))
            .ShouldBeFalse();
        _installation.FindArchives().ShouldBeEmpty();
        _installation.FindStateFiles().ShouldBeEmpty();
        (await File.ReadAllBytesAsync(_installation.BackupConfigPath, CancellationToken)).ShouldBe(config);
    }

    [Fact]
    public async Task Backup_DryRunAfterRemoteCommit_ChangesNothingOnDisk()
    {
        var remote = (await GivenTrackedRepositoriesAsync("a")).Single();
        var firstRun = await _installation.RunAsync("backup", "--silent");
        firstRun.ExitCode.ShouldBe((int)ExitCode.Success, firstRun.ToString());
        var clone = _installation.CloneFolder(_owner, "a");
        var refs = await _sandbox.RunGitAsync(clone, CancellationToken, "for-each-ref", "--format=%(refname) %(objectname)");
        var state = await File.ReadAllBytesAsync(_installation.FindStateFiles().Single(), CancellationToken);
        await remote.CommitAsync("CHANGES.md", "second", "Second commit", CancellationToken);

        var result = await _installation.RunAsync("backup", "--dry-run");

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        result.StandardOutput.ShouldContain(nameof(BackupItemStatus.WouldArchive));
        _installation.FindArchives().ShouldHaveSingleItem();
        (await _sandbox.RunGitAsync(clone, CancellationToken, "for-each-ref", "--format=%(refname) %(objectname)"))
            .ShouldBe(refs);
        (await File.ReadAllBytesAsync(_installation.FindStateFiles().Single(), CancellationToken)).ShouldBe(state);
    }

    [Fact]
    public async Task Backup_SilentRepoFilter_BacksUpOnlyThatRepository()
    {
        await GivenTrackedRepositoriesAsync("a", "b");
        await GivenNewRepositoryAsync("new", 3);

        var result = await _installation.RunAsync("backup", "--silent", "--repo", _owner + "/a");

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        _installation.FindArchives(_owner, "a").ShouldHaveSingleItem();
        _installation.FindArchives(_owner, "b").ShouldBeEmpty();
        Directory.Exists(_installation.CloneFolder(_owner, "b")).ShouldBeFalse();
        (await ReadBackupConfigAsync()).ShouldNotContain($"https://github.com/{_owner}/new");
    }

    [Fact]
    public async Task Backup_SilentAccountFilter_BacksUpOnlyThatAccount()
    {
        await _sandbox.CreateRemoteAsync(_owner + "/a", CancellationToken);
        await _sandbox.CreateRemoteAsync(_account + "/x", CancellationToken);
        var standalone = _api.GivenRepository(1, _owner, "a");
        var accountRepository = _api.GivenRepository(10, _account, "x");
        _api.GivenUserAccount(_account, accountRepository);
        _api.GivenCurrentUserRepositories(standalone, accountRepository);
        _config
            .WithAccount(_account, ("x", 10))
            .WithRepository(_owner, "a", 1)
            .WriteTo(_installation.BackupConfigPath);

        var result = await _installation.RunAsync("backup", "--silent", "--account", _account);

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        _installation.FindArchives(_account, "x").ShouldHaveSingleItem();
        _installation.FindArchives(_owner, "a").ShouldBeEmpty();
        Directory.Exists(_installation.CloneFolder(_owner, "a")).ShouldBeFalse();
    }

    /// <summary>
    /// Deletes the installation, stops the API stub and deletes the sandbox.
    /// </summary>
    public void Dispose()
    {
        _installation.Dispose();
        _api.Dispose();
        _sandbox.Dispose();
    }

    [Fact]
    public async Task Backup_SilentEntryWithoutId_WritesIdArchivesExitsSuccess()
    {
        await _sandbox.CreateRemoteAsync($"{_owner}/a", CancellationToken);
        GivenCurrentUserRepository("a", 5);
        _config.WithRepositoryWithoutId(_owner, "a").WriteTo(_installation.BackupConfigPath);
        var before = await ReadBackupConfigAsync();

        var result = await _installation.RunAsync("backup", "--silent");

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        result.StandardOutput.ShouldContain(nameof(BackupItemStatus.Archived));
        StandaloneIds(await ReadBackupConfigAsync()).ShouldBe([5]);
        (await File.ReadAllTextAsync(_installation.BackupConfigPath + ".bak", CancellationToken)).ShouldBe(before);
        _installation.FindArchives(_owner, "a").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Backup_SilentEntryWithoutIdNotOnGitHub_FailedWithoutIdOthersArchived()
    {
        await GivenTrackedRepositoriesAsync("a");
        _config.WithRepositoryWithoutId(_owner, "ghost").WriteTo(_installation.BackupConfigPath);
        var before = await File.ReadAllBytesAsync(_installation.BackupConfigPath, CancellationToken);

        var result = await _installation.RunAsync("backup", "--silent");

        result.ExitCode.ShouldBe((int)ExitCode.RepositoriesFailed, result.ToString());
        result.StandardOutput.ShouldContain(nameof(BackupItemStatus.Failed));
        (await File.ReadAllBytesAsync(_installation.BackupConfigPath, CancellationToken)).ShouldBe(before);
        _installation.FindArchives(_owner, "a").ShouldHaveSingleItem();
        _installation.FindArchives(_owner, "ghost").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("backup", "--dry-run")]
    [InlineData("backup", "--dry-run", "--silent")]
    public async Task Backup_DryRunRedirectedOrSilentEntryWithoutId_WritesOnlyId(params string[] arguments)
    {
        await _sandbox.CreateRemoteAsync($"{_owner}/a", CancellationToken);
        GivenCurrentUserRepository("a", 5);
        _config.WithRepositoryWithoutId(_owner, "a").WriteTo(_installation.BackupConfigPath);
        var before = JsonNode.Parse(await ReadBackupConfigAsync());

        var result = await _installation.RunAsync(arguments);

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        result.StandardOutput.ReplaceLineEndings(string.Empty).ShouldContain(BackupSummaryRenderer.DryRunSavedIdsHeader);
        result.StandardOutput.ShouldContain(nameof(BackupItemStatus.WouldClone));
        var saved = JsonNode.Parse(await ReadBackupConfigAsync()).ShouldNotBeNull();
        var entry = saved["repositories"].ShouldNotBeNull()[0].ShouldNotBeNull().AsObject();
        entry["id"].ShouldNotBeNull().GetValue<long>().ShouldBe(5);
        entry.Remove("id");
        JsonNode.DeepEquals(saved, before).ShouldBeTrue(saved.ToJsonString());
        Directory.Exists(_installation.SourcesPath).ShouldBeFalse();
        _installation.FindArchives().ShouldBeEmpty();
        _installation.FindStateFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task Validate_EntryWithoutId_ExitsSuccess()
    {
        _config.WithRepositoryWithoutId(_owner, "a").WriteTo(_installation.BackupConfigPath);
        var before = await File.ReadAllBytesAsync(_installation.BackupConfigPath, CancellationToken);

        var result = await _installation.RunAsync("validate");

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        (await File.ReadAllBytesAsync(_installation.BackupConfigPath, CancellationToken)).ShouldBe(before);
    }

    /// <summary>
    /// Returns the <c>id</c> of each standalone repository of a configuration, <c>0</c> for an entry without one.
    /// </summary>
    /// <param name="json">The text of <c>backup-config.json</c>.</param>
    /// <returns>The ids in file order.</returns>
    private static long[] StandaloneIds(string json) =>
    [
        .. JsonNode.Parse(json).ShouldNotBeNull()["repositories"].ShouldNotBeNull().AsArray()
            .Select(entry => entry?["id"]?.GetValue<long>() ?? 0),
    ];

    /// <summary>
    /// Creates a bare repository with one commit per name, serves each through the API and the list of the token's
    /// repositories, and writes a configuration that tracks them as standalone repositories with ids from 1 upwards.
    /// </summary>
    /// <param name="names">The names of the repositories of <see cref="_owner"/>.</param>
    /// <returns>The remotes, in the order of <paramref name="names"/>.</returns>
    private async Task<IReadOnlyList<LocalGitRemote>> GivenTrackedRepositoriesAsync(params string[] names)
    {
        List<LocalGitRemote> remotes = [];
        for (var index = 0; index < names.Length; index++)
        {
            remotes.Add(await _sandbox.CreateRemoteAsync($"{_owner}/{names[index]}", CancellationToken));
            GivenTrackedRepositoryWithoutRemote(names[index], index + 1);
        }

        return remotes;
    }

    /// <summary>
    /// Serves a repository through the API and the list of the token's repositories and tracks it in the
    /// configuration, without creating a bare repository, so cloning it fails.
    /// </summary>
    /// <param name="name">The name of the repository of <see cref="_owner"/>.</param>
    /// <param name="id">The repository id.</param>
    private void GivenTrackedRepositoryWithoutRemote(string name, long id)
    {
        GivenCurrentUserRepository(name, id);
        _config.WithRepository(_owner, name, id).WriteTo(_installation.BackupConfigPath);
    }

    /// <summary>
    /// Creates a bare repository with one commit and serves it through the API and the list of the token's
    /// repositories, without tracking it in the configuration.
    /// </summary>
    /// <param name="name">The name of the repository of <see cref="_owner"/>.</param>
    /// <param name="id">The repository id.</param>
    /// <returns>A task that completes when the repository is served.</returns>
    private async Task GivenNewRepositoryAsync(string name, long id)
    {
        await _sandbox.CreateRemoteAsync($"{_owner}/{name}", CancellationToken);
        GivenCurrentUserRepository(name, id);
    }

    /// <summary>
    /// Creates a bare repository of the owner <c>other</c> and serves it in the list of the token's repositories,
    /// without tracking it in the configuration.
    /// </summary>
    /// <param name="name">The name of the repository.</param>
    /// <param name="id">The repository id.</param>
    /// <returns>A task that completes when the repository is served.</returns>
    private async Task GivenOtherOwnerRepositoryAsync(string name, long id)
    {
        await _sandbox.CreateRemoteAsync($"other/{name}", CancellationToken);
        _currentUserRepositories.Add(_api.GivenRepository(id, "other", name));
        _api.GivenCurrentUserRepositories([.. _currentUserRepositories]);
    }

    /// <summary>
    /// Serves a repository through the API and adds it to the list of the token's repositories.
    /// </summary>
    /// <param name="name">The name of the repository of <see cref="_owner"/>.</param>
    /// <param name="id">The repository id.</param>
    private void GivenCurrentUserRepository(string name, long id)
    {
        _currentUserRepositories.Add(_api.GivenRepository(id, _owner, name));
        _api.GivenCurrentUserRepositories([.. _currentUserRepositories]);
    }

    /// <summary>
    /// Reads <c>backup-config.json</c> of the installation.
    /// </summary>
    /// <returns>The text of the file.</returns>
    private Task<string> ReadBackupConfigAsync() =>
        File.ReadAllTextAsync(_installation.BackupConfigPath, CancellationToken);
}
