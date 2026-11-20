using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Overview;

/// <summary>
/// Tests <see cref="RepositoryOverviewProvider"/> over the real path resolver and exclusion matcher.
/// </summary>
public sealed class RepositoryOverviewProviderTests
{
    private const string _backupRoot = @"C:\backup";

    private static readonly DateTimeOffset _firstCreatedAt = new(2026, 10, 2, 14, 30, 15, TimeSpan.FromHours(3));

    private static readonly DateTimeOffset _lastCreatedAt = new(2026, 11, 5, 9, 0, 0, TimeSpan.FromHours(2));

    private readonly IBackupConfigStore _configStore = Substitute.For<IBackupConfigStore>();

    private readonly IBackupStateStore _stateStore = Substitute.For<IBackupStateStore>();

    private readonly RepositoryOverviewProvider _provider;

    /// <summary>
    /// Creates the provider; every state file is missing unless a test configures it.
    /// </summary>
    public RepositoryOverviewProviderTests()
    {
        _stateStore.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => BackupState.CreateEmpty(call.ArgAt<long>(1), call.ArgAt<string>(2)));
        _provider = new(
            _configStore,
            _stateStore,
            new RepositoryPathResolver(@"C:\app\sources"),
            new ExclusionMatcher(),
            Options.Create(new BackupOptions { ConfigPath = @"C:\app\backup-config.json" }));
    }

    [Fact]
    public async Task LoadAsync_AccountsAndStandalone_ReturnsAccountsInFileOrderThenStandalone()
    {
        UseConfig(new()
        {
            BackupRoot = _backupRoot,
            Accounts =
            [
                CreateAccount("zeta", "b-repo", "a-repo"),
                CreateAccount("alpha", "tool"),
            ],
            Repositories = [CreateStandalone("other", "lib"), CreateStandalone("octo", "app")],
        });

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        overview.Accounts.Select(account => account.Login).ShouldBe(["zeta", "alpha"]);
        overview.Accounts[0].Url.ShouldBe("https://github.com/zeta");
        overview.Accounts[0].Repositories.Select(entry => entry.FullName).ShouldBe(["zeta/b-repo", "zeta/a-repo"]);
        overview.Accounts[0].Repositories.ShouldAllBe(entry => entry.AccountLogin == "zeta");
        overview.Accounts[1].Repositories.Select(entry => entry.FullName).ShouldBe(["alpha/tool"]);
        overview.StandaloneRepositories.Select(entry => entry.FullName).ShouldBe(["other/lib", "octo/app"]);
        overview.StandaloneRepositories.ShouldAllBe(entry => entry.AccountLogin == null);
        overview.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_AccountWithoutRepositories_ReturnsEmptyAccount()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo")] });

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        overview.Accounts.ShouldHaveSingleItem().Login.ShouldBe("octo");
        overview.Accounts[0].Repositories.ShouldBeEmpty();
        overview.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_OnlyStandaloneRepositories_ReturnsNoAccounts()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Repositories = [CreateStandalone("octo", "tool")] });

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        overview.Accounts.ShouldBeEmpty();
        overview.StandaloneRepositories.ShouldHaveSingleItem().FullName.ShouldBe("octo/tool");
    }

    [Fact]
    public async Task LoadAsync_EmptyConfig_ReturnsEmptyOverview()
    {
        UseConfig(new() { BackupRoot = _backupRoot });

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        overview.Accounts.ShouldBeEmpty();
        overview.StandaloneRepositories.ShouldBeEmpty();
        overview.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_ConfigStoreThrows_PropagatesBackupConfigException()
    {
        var error = new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set."]);
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(error);

        var exception = await Should.ThrowAsync<BackupConfigException>(
            () => _provider.LoadAsync(TestContext.Current.CancellationToken));

        exception.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task LoadAsync_ConfigNotFound_PropagatesNotFoundException()
    {
        var error = new BackupConfigNotFoundException(@"C:\app\backup-config.json");
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(error);

        var exception = await Should.ThrowAsync<BackupConfigNotFoundException>(
            () => _provider.LoadAsync(TestContext.Current.CancellationToken));

        exception.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task LoadAsync_InvalidRepositoryUrl_ThrowsBackupConfigExceptionWithConfigPath()
    {
        UseConfig(new()
        {
            BackupRoot = _backupRoot,
            Repositories = [new() { Url = "https://example.com/octo/tool", Id = 1 }],
        });

        var exception = await Should.ThrowAsync<BackupConfigException>(
            () => _provider.LoadAsync(TestContext.Current.CancellationToken));

        exception.ConfigPath.ShouldBe(@"C:\app\backup-config.json");
        exception.Errors.ShouldHaveSingleItem().ShouldContain("https://example.com/octo/tool");
        exception.InnerException.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_RepositoryWithArchives_ReturnsCreatedAtOfLastRepositoryArchive()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Repositories = [CreateStandalone("octo", "tool", id: 7)] });
        var state = BackupState.CreateEmpty(7, "octo/tool")
            .WithArchive(BackupTarget.Repository, CreateRecord("first.zip", _firstCreatedAt), "octo/tool")
            .WithArchive(BackupTarget.Repository, CreateRecord("last.zip", _lastCreatedAt), "octo/tool");
        _stateStore.LoadAsync(@"C:\backup\octo\tool", 7, "octo/tool", Arg.Any<CancellationToken>()).Returns(state);

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        var entry = overview.StandaloneRepositories.ShouldHaveSingleItem();
        entry.LastArchiveAt.ShouldBe(_lastCreatedAt);
        entry.StateError.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_Repositories_ReturnIdAndArchiveFolderOfRun()
    {
        UseConfig(new()
        {
            BackupRoot = _backupRoot,
            Accounts = [CreateAccount("octo", "tool")],
            Repositories = [CreateStandalone("other", "lib", id: 42)],
        });

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        var account = overview.Accounts[0].Repositories.ShouldHaveSingleItem();
        account.RepositoryId.ShouldBe(1);
        account.ArchiveFolder.ShouldBe(@"C:\backup\octo\tool");
        var standalone = overview.StandaloneRepositories.ShouldHaveSingleItem();
        standalone.RepositoryId.ShouldBe(42);
        standalone.ArchiveFolder.ShouldBe(@"C:\backup\other\lib");
    }

    [Fact]
    public async Task LoadAsync_NoStateFile_ReturnsNullLastArchive()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool")] });

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        var entry = overview.Accounts[0].Repositories.ShouldHaveSingleItem();
        entry.LastArchiveAt.ShouldBeNull();
        entry.StateError.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_StateStoreThrows_ReturnsStateErrorAndKeepsOtherEntries()
    {
        UseConfig(new()
        {
            BackupRoot = _backupRoot,
            Accounts = [CreateAccount("octo", "broken", "fine")],
        });
        var error = new BackupStateException(@"C:\backup\octo\broken\.backup-state.json", ["The file is not valid JSON."]);
        _stateStore.LoadAsync(@"C:\backup\octo\broken", Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);
        var fineState = BackupState.CreateEmpty(2, "octo/fine")
            .WithArchive(BackupTarget.Repository, CreateRecord("fine.zip", _lastCreatedAt), "octo/fine");
        _stateStore.LoadAsync(@"C:\backup\octo\fine", Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(fineState);

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        var entries = overview.Accounts[0].Repositories;
        entries[0].LastArchiveAt.ShouldBeNull();
        entries[0].StateError.ShouldBe(error.Message);
        entries[1].LastArchiveAt.ShouldBe(_lastCreatedAt);
        entries[1].StateError.ShouldBeNull();
    }

    [Theory]
    [InlineData(RepositoryStatus.Active, null, null, RepositoryOverviewStatus.Active)]
    [InlineData(RepositoryStatus.Unavailable, null, null, RepositoryOverviewStatus.Unavailable)]
    [InlineData(RepositoryStatus.Active, "octo/t*", null, RepositoryOverviewStatus.Excluded)]
    [InlineData(RepositoryStatus.Unavailable, "octo/t*", null, RepositoryOverviewStatus.Excluded)]
    [InlineData(RepositoryStatus.Active, null, "to?l", RepositoryOverviewStatus.Excluded)]
    public async Task LoadAsync_Status_ReflectsExclusionAndAvailability(
        RepositoryStatus status, string? globalMask, string? accountMask, RepositoryOverviewStatus expected)
    {
        var account = CreateAccount("octo", "tool");
        account.Repositories[0].Status = status;
        if (accountMask is not null)
        {
            account.Exclude.Add(accountMask);
        }

        UseConfig(new()
        {
            BackupRoot = _backupRoot,
            Exclude = globalMask is null ? [] : [globalMask],
            Accounts = [account],
        });

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        overview.Accounts[0].Repositories.ShouldHaveSingleItem().Status.ShouldBe(expected);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LoadAsync_AccountRepository_ReflectsPauseOfEntryAndAccount(bool entryPaused, bool accountPaused)
    {
        var account = CreateAccount("octo", "tool");
        account.Paused = accountPaused;
        account.Repositories[0].Paused = entryPaused;
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [account] });

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        overview.Accounts[0].IsPaused.ShouldBe(accountPaused);
        var entry = overview.Accounts[0].Repositories.ShouldHaveSingleItem();
        entry.IsPaused.ShouldBe(entryPaused);
        entry.IsAccountPaused.ShouldBe(accountPaused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_StandaloneRepository_ReflectsPauseOfEntry(bool paused)
    {
        var repository = CreateStandalone("octo", "app");
        repository.Paused = paused;
        UseConfig(new() { BackupRoot = _backupRoot, Repositories = [repository] });

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        var entry = overview.StandaloneRepositories.ShouldHaveSingleItem();
        entry.IsPaused.ShouldBe(paused);
        entry.IsAccountPaused.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAfterRunAsync_PausedEntries_KeepsPauseFlags()
    {
        var account = CreateAccount("octo", "tool");
        account.Paused = true;
        var repository = CreateStandalone("other", "lib");
        repository.Paused = true;
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [account], Repositories = [repository] });
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        var overview = await _provider.LoadAfterRunAsync(previous, CreateResult(), TestContext.Current.CancellationToken);

        overview.Accounts[0].IsPaused.ShouldBeTrue();
        overview.Accounts[0].Repositories[0].IsAccountPaused.ShouldBeTrue();
        overview.StandaloneRepositories[0].IsPaused.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_WikiArchiveOnly_IgnoresWikiSection()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Repositories = [CreateStandalone("octo", "tool", id: 7)] });
        var state = BackupState.CreateEmpty(7, "octo/tool")
            .WithArchive(BackupTarget.Wiki, CreateRecord("wiki.zip", _lastCreatedAt), "octo/tool");
        _stateStore.LoadAsync(@"C:\backup\octo\tool", 7, "octo/tool", Arg.Any<CancellationToken>()).Returns(state);

        var overview = await _provider.LoadAsync(TestContext.Current.CancellationToken);

        overview.StandaloneRepositories.ShouldHaveSingleItem().LastArchiveAt.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAfterRunAsync_ProcessedRepository_ReadsItsState()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool", "lib")] });
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);
        _stateStore.ClearReceivedCalls();
        var state = BackupState.CreateEmpty(1, "octo/tool")
            .WithArchive(BackupTarget.Repository, CreateRecord("new.zip", _lastCreatedAt), "octo/tool");
        _stateStore.LoadAsync(@"C:\backup\octo\tool", 1, "octo/tool", Arg.Any<CancellationToken>()).Returns(state);

        var overview = await _provider.LoadAfterRunAsync(
            previous, CreateResult("octo/tool"), TestContext.Current.CancellationToken);

        overview.Accounts[0].Repositories[0].LastArchiveAt.ShouldBe(_lastCreatedAt);
        await _stateStore.Received(1).LoadAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAfterRunAsync_UnprocessedRepository_KeepsPreviousStateWithoutReading()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool", "broken")] });
        _stateStore.LoadAsync(@"C:\backup\octo\tool", 1, "octo/tool", Arg.Any<CancellationToken>())
            .Returns(BackupState.CreateEmpty(1, "octo/tool")
                .WithArchive(BackupTarget.Repository, CreateRecord("a.zip", _lastCreatedAt), "octo/tool"));
        _stateStore.LoadAsync(@"C:\backup\octo\broken", 2, "octo/broken", Arg.Any<CancellationToken>())
            .ThrowsAsync(new BackupStateException(@"C:\backup\octo\broken\.backup-state.json", ["Bad JSON."]));
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);
        _stateStore.ClearReceivedCalls();

        var overview = await _provider.LoadAfterRunAsync(
            previous, CreateResult(), TestContext.Current.CancellationToken);

        var entries = overview.Accounts[0].Repositories;
        entries[0].LastArchiveAt.ShouldBe(_lastCreatedAt);
        entries[1].StateError.ShouldNotBeNull();
        await _stateStore.DidNotReceiveWithAnyArgs().LoadAsync(default!, default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LoadAfterRunAsync_NewEntry_ReadsItsState()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool")] });
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);
        _stateStore.ClearReceivedCalls();
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool", "lib")] });

        var overview = await _provider.LoadAfterRunAsync(
            previous, CreateResult(), TestContext.Current.CancellationToken);

        overview.Accounts[0].Repositories.Select(entry => entry.Name).ShouldBe(["tool", "lib"]);
        await _stateStore.Received(1).LoadAsync(
            @"C:\backup\octo\lib", 2, "octo/lib", Arg.Any<CancellationToken>());
        await _stateStore.Received(1).LoadAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAfterRunAsync_ChangedArchiveFolder_ReadsItsState()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool")] });
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);
        _stateStore.ClearReceivedCalls();
        UseConfig(new() { BackupRoot = @"C:\other", Accounts = [CreateAccount("octo", "tool")] });

        var overview = await _provider.LoadAfterRunAsync(
            previous, CreateResult(), TestContext.Current.CancellationToken);

        overview.Accounts[0].Repositories[0].ArchiveFolder.ShouldBe(@"C:\other\octo\tool");
        await _stateStore.Received(1).LoadAsync(
            @"C:\other\octo\tool", 1, "octo/tool", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAfterRunAsync_WikiResult_ReadsStateOfItsRepository()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool", "lib")] });
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);
        _stateStore.ClearReceivedCalls();

        await _provider.LoadAfterRunAsync(
            previous, CreateResult("octo/TOOL.wiki", BackupTarget.Wiki), TestContext.Current.CancellationToken);

        await _stateStore.Received(1).LoadAsync(
            @"C:\backup\octo\tool", 1, "octo/tool", Arg.Any<CancellationToken>());
        await _stateStore.Received(1).LoadAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAfterRunAsync_EntryWithoutId_MatchesPreviousByName()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Repositories = [CreateStandalone("octo", "tool", id: 0)] });
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);
        _stateStore.ClearReceivedCalls();

        await _provider.LoadAfterRunAsync(previous, CreateResult(), TestContext.Current.CancellationToken);

        await _stateStore.DidNotReceiveWithAnyArgs().LoadAsync(default!, default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LoadAfterRunAsync_ExcludedByNewConfiguration_RecomputesStatus()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool")] });
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);
        UseConfig(new() { BackupRoot = _backupRoot, Exclude = ["octo/tool"], Accounts = [CreateAccount("octo", "tool")] });

        var overview = await _provider.LoadAfterRunAsync(
            previous, CreateResult(), TestContext.Current.CancellationToken);

        overview.Accounts[0].Repositories.ShouldHaveSingleItem().Status.ShouldBe(RepositoryOverviewStatus.Excluded);
    }

    [Fact]
    public async Task LoadAfterRunAsync_RemovedEntry_IsAbsent()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool", "lib")] });
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool")] });

        var overview = await _provider.LoadAfterRunAsync(
            previous, CreateResult(), TestContext.Current.CancellationToken);

        overview.Accounts[0].Repositories.Select(entry => entry.Name).ShouldBe(["tool"]);
    }

    [Fact]
    public async Task LoadAfterRunAsync_InvalidConfiguration_ThrowsBackupConfigException()
    {
        UseConfig(new() { BackupRoot = _backupRoot, Accounts = [CreateAccount("octo", "tool")] });
        var previous = await _provider.LoadAsync(TestContext.Current.CancellationToken);
        var error = new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set."]);
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(error);

        var exception = await Should.ThrowAsync<BackupConfigException>(
            () => _provider.LoadAfterRunAsync(previous, CreateResult(), TestContext.Current.CancellationToken));

        exception.ShouldBeSameAs(error);
    }

    /// <summary>
    /// Creates a run result that processed the given repository.
    /// </summary>
    /// <param name="repository">The name of the processed repository as the result carries it, or none.</param>
    /// <param name="target">What the result is about.</param>
    /// <returns>The result.</returns>
    private static BackupRunResult CreateResult(string? repository = null, BackupTarget target = BackupTarget.Repository) =>
        new(
            repository is null
                ? []
                : [new(repository, BackupItemStatus.Archived, null, TimeSpan.Zero, null) { Target = target }],
            [],
            []);

    /// <summary>
    /// Makes the configuration store return <paramref name="config"/>.
    /// </summary>
    /// <param name="config">The configuration.</param>
    private void UseConfig(BackupConfig config) =>
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(config, []));

    /// <summary>
    /// Creates a tracked account with active repositories whose ids follow their position, starting at 1.
    /// </summary>
    /// <param name="login">The login of the account.</param>
    /// <param name="repositories">The names of the repositories.</param>
    /// <returns>The account.</returns>
    private static AccountConfig CreateAccount(string login, params string[] repositories) => new()
    {
        Url = $"https://github.com/{login}",
        Repositories = [.. repositories.Select((name, index) => new AccountRepositoryConfig { Name = name, Id = index + 1 })],
    };

    /// <summary>
    /// Creates an active standalone repository.
    /// </summary>
    /// <param name="owner">The owner of the repository.</param>
    /// <param name="name">The name of the repository.</param>
    /// <param name="id">The GitHub id of the repository.</param>
    /// <returns>The repository.</returns>
    private static RepositoryConfig CreateStandalone(string owner, string name, long id = 100) =>
        new() { Url = $"https://github.com/{owner}/{name}", Id = id };

    /// <summary>
    /// Creates an archive record created at <paramref name="createdAt"/>.
    /// </summary>
    /// <param name="file">The file name of the archive.</param>
    /// <param name="createdAt">The local time the archive was created.</param>
    /// <returns>The record.</returns>
    private static ArchiveRecord CreateRecord(string file, DateTimeOffset createdAt) =>
        new(file, createdAt, "main", "a1b2c3", 10, RefSnapshot.Empty);
}
