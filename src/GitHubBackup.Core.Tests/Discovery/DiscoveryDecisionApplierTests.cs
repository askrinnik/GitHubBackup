using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Discovery;

/// <summary>
/// Tests <see cref="DiscoveryDecisionApplier"/>.
/// </summary>
public sealed class DiscoveryDecisionApplierTests
{
    private const string _gitPath = @"C:\git\git.exe";
    private const int _markedUnavailableEventId = 3;
    private const int _reactivatedEventId = 4;
    private const int _archiveFolderTargetExistsEventId = 7;

    private readonly MockFileSystem _fileSystem = new();
    private readonly IBackupConfigStore _store = Substitute.For<IBackupConfigStore>();
    private readonly ICloneRelocator _relocator = Substitute.For<ICloneRelocator>();
    private readonly IDiscoveryDecisionHandler _handler = Substitute.For<IDiscoveryDecisionHandler>();
    private readonly FakeLogger<DiscoveryDecisionApplier> _logger = new();
    private readonly DiscoveryDecisionApplier _applier;

    public DiscoveryDecisionApplierTests() =>
        _applier = new(
            _store,
            new RepositoryPathResolver(@"C:\app\sources"),
            new GitHubRemoteUrlProvider(),
            _relocator,
            _fileSystem,
            _logger);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null, null)]
    [InlineData("develop", @"D:\archives\tool")]
    public async Task ApplyAsync_NewInAccountAdded_AddsEntryAndSaves(string? branch, string? path)
    {
        var config = Config();
        _handler.DecideNewAsync(default!, Token).ReturnsForAnyArgs(NewRepositoryDecision.Add(branch, path));

        var result = await ApplyAsync(config, DiscoveryResult.Empty with { NewInAccounts = [new("octo", Repo(10, "octo", "tool"))] });

        var entry = config.Accounts[0].Repositories.ShouldHaveSingleItem();
        entry.Name.ShouldBe("tool");
        entry.Id.ShouldBe(10);
        entry.Branch.ShouldBe(branch);
        entry.Path.ShouldBe(path);
        entry.Status.ShouldBe(RepositoryStatus.Active);
        result.Added.ShouldBe(["octo/tool"]);
        result.ConfigSaved.ShouldBeTrue();
        await _store.Received(1).SaveAsync(config, Token);
    }

    [Fact]
    public async Task ApplyAsync_NewInAccountSkipped_DoesNotSave()
    {
        var config = Config();
        _handler.DecideNewAsync(default!, Token).ReturnsForAnyArgs(NewRepositoryDecision.Skip);

        var result = await ApplyAsync(config, DiscoveryResult.Empty with { NewInAccounts = [new("octo", Repo(10, "octo", "tool"))] });

        config.Accounts[0].Repositories.ShouldBeEmpty();
        result.Skipped.ShouldBe(["octo/tool"]);
        result.ConfigSaved.ShouldBeFalse();
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Token);
    }

    [Theory]
    [InlineData(ExclusionScope.Account)]
    [InlineData(ExclusionScope.Global)]
    public async Task ApplyAsync_NewInAccountExcluded_AddsMaskToChosenList(ExclusionScope scope)
    {
        var config = Config();
        _handler.DecideNewAsync(default!, Token).ReturnsForAnyArgs(NewRepositoryDecision.Exclude(scope));

        var result = await ApplyAsync(config, DiscoveryResult.Empty with { NewInAccounts = [new("octo", Repo(10, "octo", "tool"))] });

        config.Accounts[0].Exclude.ShouldBe(scope == ExclusionScope.Account ? ["tool"] : []);
        config.Exclude.ShouldBe(scope == ExclusionScope.Global ? ["octo/tool"] : []);
        config.Accounts[0].Repositories.ShouldBeEmpty();
        result.Excluded.ShouldBe(["octo/tool"]);
        await _store.Received(1).SaveAsync(config, Token);
    }

    [Fact]
    public async Task ApplyAsync_MaskAlreadyPresent_DoesNotDuplicateItOrSave()
    {
        var config = Config();
        config.Accounts[0].Exclude.Add("TOOL");
        _handler.DecideNewAsync(default!, Token).ReturnsForAnyArgs(NewRepositoryDecision.Exclude(ExclusionScope.Account));

        var result = await ApplyAsync(config, DiscoveryResult.Empty with { NewInAccounts = [new("octo", Repo(10, "octo", "tool"))] });

        config.Accounts[0].Exclude.ShouldBe(["TOOL"]);
        result.ConfigSaved.ShouldBeFalse();
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Token);
    }

    [Fact]
    public async Task ApplyAsync_NewStandaloneAdded_AddsRepositoryWithUrl()
    {
        var config = Config();
        _handler.DecideNewAsync(default!, Token).ReturnsForAnyArgs(NewRepositoryDecision.Add("dev"));

        await ApplyAsync(config, DiscoveryResult.Empty with { NewStandalone = [Repo(20, "acme", "lib")] });

        var entry = config.Repositories.ShouldHaveSingleItem();
        entry.Url.ShouldBe("https://github.com/acme/lib");
        entry.Id.ShouldBe(20);
        entry.Branch.ShouldBe("dev");
        entry.Status.ShouldBe(RepositoryStatus.Active);
        await _store.Received(1).SaveAsync(config, Token);
    }

    [Fact]
    public async Task ApplyAsync_NewStandaloneExcludedGlobally_AddsGlobalMask()
    {
        var config = Config();
        _handler.DecideNewAsync(default!, Token).ReturnsForAnyArgs(NewRepositoryDecision.Exclude(ExclusionScope.Global));

        await ApplyAsync(config, DiscoveryResult.Empty with { NewStandalone = [Repo(20, "acme", "lib")] });

        config.Exclude.ShouldBe(["acme/lib"]);
        config.Repositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_NewStandaloneExcludedInAccount_ThrowsWithoutChanges()
    {
        var config = Config();
        var before = Snapshot(config);
        _handler.DecideNewAsync(default!, Token).ReturnsForAnyArgs(NewRepositoryDecision.Exclude(ExclusionScope.Account));

        await Should.ThrowAsync<ArgumentException>(
            () => ApplyAsync(config, DiscoveryResult.Empty with { NewStandalone = [Repo(20, "acme", "lib")] }));

        Snapshot(config).ShouldBe(before);
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Token);
    }

    [Fact]
    public async Task ApplyAsync_NewRepositories_ProposalsCarryRepositoryAccountAndDefaultArchiveFolder()
    {
        var config = Config();
        var proposals = new List<NewRepositoryProposal>();
        _handler.DecideNewAsync(Arg.Do<NewRepositoryProposal>(proposals.Add), Token)
            .ReturnsForAnyArgs(NewRepositoryDecision.Skip);
        var accountRepository = Repo(10, "octo", "tool");
        var standalone = Repo(20, "acme", "lib");

        await ApplyAsync(
            config,
            DiscoveryResult.Empty with { NewInAccounts = [new("octo", accountRepository)], NewStandalone = [standalone] });

        proposals.ShouldBe(
        [
            new(accountRepository, "octo", @"C:\backup\octo\tool"),
            new(standalone, null, @"C:\backup\acme\lib"),
        ]);
    }

    [Fact]
    public async Task ApplyAsync_Missing_MarksUnavailableAndKeepsFiles()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1 });
        _fileSystem.AddFile(@"C:\backup\octo\app\app.zip", new("zip"));
        _fileSystem.AddFile(@"C:\backup\octo\app\.backup-state.json", new("{}"));
        _fileSystem.AddFile(@"C:\app\sources\octo\app\.git\HEAD", new("ref"));

        var result = await ApplyAsync(config, DiscoveryResult.Empty with { Missing = [Tracked("octo", "app", 1, "octo")] });

        config.Accounts[0].Repositories[0].Status.ShouldBe(RepositoryStatus.Unavailable);
        result.MarkedUnavailable.ShouldBe(["octo/app"]);
        _fileSystem.AllFiles.Count().ShouldBe(3);
        await _relocator.DidNotReceiveWithAnyArgs().RelocateAsync(default!, default!, default!, default!, Token);
        await _store.Received(1).SaveAsync(config, Token);
        var record = SingleRecord(_markedUnavailableEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Repository").ShouldBe("octo/app");
    }

    [Fact]
    public async Task ApplyAsync_Returned_MarksActiveAndReports()
    {
        var config = Config();
        config.Repositories.Add(new() { Url = "https://github.com/acme/lib", Id = 2, Status = RepositoryStatus.Unavailable });
        var tracked = Tracked("acme", "lib", 2, null, RepositoryStatus.Unavailable);

        var result = await ApplyAsync(
            config, DiscoveryResult.Empty with { Returned = [new(tracked, Repo(2, "acme", "lib"))] });

        config.Repositories[0].Status.ShouldBe(RepositoryStatus.Active);
        result.Reactivated.ShouldBe(["acme/lib"]);
        await _store.Received(1).SaveAsync(config, Token);
        var record = SingleRecord(_reactivatedEventId);
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Repository").ShouldBe("acme/lib");
    }

    [Fact]
    public async Task ApplyAsync_ReturnedAndRenamed_IsActiveUnderNewName()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1, Status = RepositoryStatus.Unavailable });
        var tracked = Tracked("octo", "app", 1, "octo", RepositoryStatus.Unavailable);
        var current = Repo(1, "octo", "app2");
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);

        await ApplyAsync(
            config, DiscoveryResult.Empty with { Returned = [new(tracked, current)], Renamed = [new(tracked, current)] });

        var entry = config.Accounts[0].Repositories.ShouldHaveSingleItem();
        entry.Name.ShouldBe("app2");
        entry.Status.ShouldBe(RepositoryStatus.Active);
    }

    [Fact]
    public async Task ApplyAsync_RenameKeepingArchiveFolder_RenamesEntryAndWritesOldFolderToPath()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1 });
        _fileSystem.AddFile(@"C:\backup\octo\app\app.zip", new("zip"));
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.KeepArchiveFolder);

        var result = await ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "octo", "app2")));

        var entry = config.Accounts[0].Repositories.ShouldHaveSingleItem();
        entry.Name.ShouldBe("app2");
        entry.Path.ShouldBe(@"C:\backup\octo\app");
        _fileSystem.File.Exists(@"C:\backup\octo\app\app.zip").ShouldBeTrue();
        result.Renamed.ShouldHaveSingleItem().Applied.ShouldBe(RenameDecision.KeepArchiveFolder);
    }

    [Fact]
    public async Task ApplyAsync_RenameWithArchiveFolder_MovesFolderAndKeepsPathUnset()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1 });
        _fileSystem.AddFile(@"C:\backup\octo\app\app.zip", new("zip"));
        RenameProposal? proposal = null;
        _handler.DecideRenameAsync(Arg.Do<RenameProposal>(value => proposal = value), Token)
            .ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);

        var result = await ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "octo", "app2")));

        proposal.ShouldNotBeNull();
        proposal.CurrentArchiveFolder.ShouldBe(@"C:\backup\octo\app");
        proposal.RenamedArchiveFolder.ShouldBe(@"C:\backup\octo\app2");
        var entry = config.Accounts[0].Repositories.ShouldHaveSingleItem();
        entry.Name.ShouldBe("app2");
        entry.Path.ShouldBeNull();
        _fileSystem.File.Exists(@"C:\backup\octo\app2\app.zip").ShouldBeTrue();
        _fileSystem.Directory.Exists(@"C:\backup\octo\app").ShouldBeFalse();
        result.Renamed.ShouldHaveSingleItem().Applied.ShouldBe(RenameDecision.RenameArchiveFolder);
    }

    [Fact]
    public async Task ApplyAsync_RenameWithArchiveFolderThatDoesNotExist_UpdatesNameOnly()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1 });
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);

        var result = await ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "octo", "app2")));

        var entry = config.Accounts[0].Repositories.ShouldHaveSingleItem();
        entry.Name.ShouldBe("app2");
        entry.Path.ShouldBeNull();
        _fileSystem.Directory.Exists(@"C:\backup\octo\app2").ShouldBeFalse();
        result.Renamed.ShouldHaveSingleItem().Applied.ShouldBe(RenameDecision.RenameArchiveFolder);
    }

    [Fact]
    public async Task ApplyAsync_RenameWithArchiveFolderWhoseTargetExists_FallsBackToKeepingFolder()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1 });
        _fileSystem.AddFile(@"C:\backup\octo\app\app.zip", new("zip"));
        _fileSystem.AddFile(@"C:\backup\octo\app2\other.zip", new("other"));
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);

        var result = await ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "octo", "app2")));

        config.Accounts[0].Repositories[0].Path.ShouldBe(@"C:\backup\octo\app");
        _fileSystem.File.Exists(@"C:\backup\octo\app\app.zip").ShouldBeTrue();
        var outcome = result.Renamed.ShouldHaveSingleItem();
        outcome.Requested.ShouldBe(RenameDecision.RenameArchiveFolder);
        outcome.Applied.ShouldBe(RenameDecision.KeepArchiveFolder);
        var record = SingleRecord(_archiveFolderTargetExistsEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("OldFolder").ShouldBe(@"C:\backup\octo\app");
        record.GetStructuredStateValue("NewFolder").ShouldBe(@"C:\backup\octo\app2");
    }

    [Fact]
    public async Task ApplyAsync_RenameWithArchiveFolderThatCannotBeMoved_FallsBackToKeepingFolder()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1 });
        _fileSystem.AddFile(@"C:\backup\octo\app\app.zip", new("zip"));
        _fileSystem.AddFile(@"C:\backup\acme", new("a file where the owner folder belongs"));
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);

        var result = await ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "acme", "app")));

        var entry = config.Repositories.ShouldHaveSingleItem();
        entry.Path.ShouldBe(@"C:\backup\octo\app");
        _fileSystem.File.Exists(@"C:\backup\octo\app\app.zip").ShouldBeTrue();
        result.Renamed.ShouldHaveSingleItem().Applied.ShouldBe(RenameDecision.KeepArchiveFolder);
    }

    [Theory]
    [InlineData(RenameDecision.RenameArchiveFolder)]
    [InlineData(RenameDecision.KeepArchiveFolder)]
    public async Task ApplyAsync_RenameOfEntryWithPath_LeavesArchiveFolderAndPathAlone(RenameDecision decision)
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1, Path = @"D:\keep\app" });
        _fileSystem.AddFile(@"D:\keep\app\app.zip", new("zip"));
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(decision);

        await ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "octo", "app2")));

        var entry = config.Accounts[0].Repositories.ShouldHaveSingleItem();
        entry.Name.ShouldBe("app2");
        entry.Path.ShouldBe(@"D:\keep\app");
        _fileSystem.File.Exists(@"D:\keep\app\app.zip").ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyAsync_OwnerChangedToTrackedAccount_MovesEntryToThatAccount()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1, Branch = "dev" });
        config.Accounts.Add(new() { Url = "https://github.com/acme" });
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);

        await ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "acme", "app")));

        config.Accounts[0].Repositories.ShouldBeEmpty();
        var entry = config.Accounts[1].Repositories.ShouldHaveSingleItem();
        entry.Name.ShouldBe("app");
        entry.Id.ShouldBe(1);
        entry.Branch.ShouldBe("dev");
        config.Repositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_OwnerChangedToUntrackedOwner_MovesEntryToStandaloneRepositories()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1, Branch = "dev" });
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);

        await ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "acme", "app")));

        config.Accounts[0].Repositories.ShouldBeEmpty();
        var entry = config.Repositories.ShouldHaveSingleItem();
        entry.Url.ShouldBe("https://github.com/acme/app");
        entry.Id.ShouldBe(1);
        entry.Branch.ShouldBe("dev");
    }

    [Fact]
    public async Task ApplyAsync_StandaloneRenamed_UpdatesUrl()
    {
        var config = Config();
        config.Repositories.Add(new() { Url = "https://github.com/acme/lib", Id = 2 });
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);

        await ApplyAsync(config, Renamed(Tracked("acme", "lib", 2, null), Repo(2, "acme2", "lib2")));

        var entry = config.Repositories.ShouldHaveSingleItem();
        entry.Url.ShouldBe("https://github.com/acme2/lib2");
        entry.Path.ShouldBeNull();
    }

    [Fact]
    public async Task ApplyAsync_Rename_RelocatesCloneAndWikiAfterSave()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1 });
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);
        _relocator.RelocateAsync(_gitPath, @"C:\app\sources\octo\app", Arg.Any<string>(), Arg.Any<string>(), Token)
            .Returns(CloneRelocationResult.Moved);
        _relocator.RelocateAsync(_gitPath, @"C:\app\sources\octo\app.wiki", Arg.Any<string>(), Arg.Any<string>(), Token)
            .Returns(CloneRelocationResult.NotPresent);

        var result = await ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "octo", "app2")));

        Received.InOrder(() =>
        {
            _store.SaveAsync(config, Token);
            _relocator.RelocateAsync(
                _gitPath, @"C:\app\sources\octo\app", @"C:\app\sources\octo\app2", "https://github.com/octo/app2.git", Token);
            _relocator.RelocateAsync(
                _gitPath,
                @"C:\app\sources\octo\app.wiki",
                @"C:\app\sources\octo\app2.wiki",
                "https://github.com/octo/app2.wiki.git",
                Token);
        });
        result.Renamed.ShouldBe(
            [new("octo/app", "octo/app2", RenameDecision.RenameArchiveFolder, RenameDecision.RenameArchiveFolder, CloneRelocationResult.Moved, CloneRelocationResult.NotPresent)]);
    }

    [Fact]
    public async Task ApplyAsync_SaveFails_RestoresArchiveFolderAndDoesNotRelocate()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1 });
        _fileSystem.AddFile(@"C:\backup\octo\app\app.zip", new("zip"));
        _handler.DecideRenameAsync(default!, Token).ReturnsForAnyArgs(RenameDecision.RenameArchiveFolder);
        _store.SaveAsync(default!, Token).ThrowsAsyncForAnyArgs(new IOException("disk full"));

        await Should.ThrowAsync<IOException>(
            () => ApplyAsync(config, Renamed(Tracked("octo", "app", 1, "octo"), Repo(1, "octo", "app2"))));

        _fileSystem.File.Exists(@"C:\backup\octo\app\app.zip").ShouldBeTrue();
        _fileSystem.Directory.Exists(@"C:\backup\octo\app2").ShouldBeFalse();
        await _relocator.DidNotReceiveWithAnyArgs().RelocateAsync(default!, default!, default!, default!, Token);
    }

    [Fact]
    public async Task ApplyAsync_EmptyResult_DoesNotSave()
    {
        var result = await ApplyAsync(Config(), DiscoveryResult.Empty);

        result.ConfigSaved.ShouldBeFalse();
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Token);
    }

    [Fact]
    public async Task ApplyAsync_HandlerCancels_LeavesConfigAndDiskUnchanged()
    {
        var config = Config(new AccountRepositoryConfig { Name = "app", Id = 1 });
        config.Accounts[0].Repositories.Add(new() { Name = "gone", Id = 3 });
        _fileSystem.AddFile(@"C:\backup\octo\app\app.zip", new("zip"));
        var before = Snapshot(config);
        _handler.DecideNewAsync(default!, Token).ReturnsForAnyArgs(NewRepositoryDecision.Add());
        _handler.DecideRenameAsync(default!, Token).ThrowsAsyncForAnyArgs(new OperationCanceledException());
        var discovery = DiscoveryResult.Empty with
        {
            NewInAccounts = [new("octo", Repo(10, "octo", "tool"))],
            Missing = [Tracked("octo", "gone", 3, "octo")],
            Renamed = [new(Tracked("octo", "app", 1, "octo"), Repo(1, "octo", "app2"))],
        };

        await Should.ThrowAsync<OperationCanceledException>(() => ApplyAsync(config, discovery));

        Snapshot(config).ShouldBe(before);
        _fileSystem.File.Exists(@"C:\backup\octo\app\app.zip").ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Token);
        await _relocator.DidNotReceiveWithAnyArgs().RelocateAsync(default!, default!, default!, default!, Token);
    }

    /// <summary>
    /// Runs the applier with the handler of the test.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="discovery">The discovery result.</param>
    /// <returns>The result of the applier.</returns>
    private Task<DiscoveryApplyResult> ApplyAsync(BackupConfig config, DiscoveryResult discovery) =>
        _applier.ApplyAsync(config, discovery, _handler, _gitPath, Token);

    /// <summary>
    /// Returns the only log record with the event id <paramref name="eventId"/> and fails if there is not exactly one.
    /// </summary>
    /// <param name="eventId">The event id of the <see cref="LoggerMessageAttribute"/>.</param>
    /// <returns>The record.</returns>
    private FakeLogRecord SingleRecord(int eventId) =>
        _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId).ShouldHaveSingleItem();

    /// <summary>
    /// Creates a configuration with the tracked account <c>octo</c> and the given repositories of it.
    /// </summary>
    /// <param name="repositories">The repositories of the account.</param>
    /// <returns>The configuration.</returns>
    private static BackupConfig Config(params AccountRepositoryConfig[] repositories) => new()
    {
        BackupRoot = @"C:\backup",
        Accounts = [new() { Url = "https://github.com/octo", Repositories = [.. repositories] }],
    };

    /// <summary>
    /// Creates a GitHub repository with a wiki.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The name.</param>
    /// <returns>The repository.</returns>
    private static GitHubRepository Repo(long id, string owner, string name) =>
        new(id, owner, name, false, false, false, true, "main", $"https://github.com/{owner}/{name}");

    /// <summary>
    /// Creates a tracked repository.
    /// </summary>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The configured name.</param>
    /// <param name="id">The id.</param>
    /// <param name="accountLogin">The account login, or <see langword="null"/> for a standalone entry.</param>
    /// <param name="status">The configured status.</param>
    /// <returns>The tracked repository.</returns>
    private static TrackedRepository Tracked(
        string owner, string name, long id, string? accountLogin, RepositoryStatus status = RepositoryStatus.Active) =>
        new(owner, name, id, status, accountLogin);

    /// <summary>
    /// Creates a discovery result with one renamed repository.
    /// </summary>
    /// <param name="tracked">The configured entry.</param>
    /// <param name="current">The repository under its new name.</param>
    /// <returns>The discovery result.</returns>
    private static DiscoveryResult Renamed(TrackedRepository tracked, GitHubRepository current) =>
        DiscoveryResult.Empty with { Renamed = [new(tracked, current)] };

    /// <summary>
    /// Serializes a configuration so that two states can be compared.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The JSON text.</returns>
    private static string Snapshot(BackupConfig config) => JsonSerializer.Serialize(config);
}
