using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace GitHubBackup.Core.Tests.Discovery;

/// <summary>
/// Tests <see cref="RepositoryDiscoveryService"/>.
/// </summary>
public sealed class RepositoryDiscoveryServiceTests
{
    private const int _invalidNameEventId = 3;
    private const int _idResolvedEventId = 4;
    private const int _idNotFoundEventId = 5;
    private const int _idConflictEventId = 6;

    private readonly IGitHubApi _api = Substitute.For<IGitHubApi>();
    private readonly FakeLogger<RepositoryDiscoveryService> _logger = new();
    private readonly RepositoryDiscoveryService _service;

    public RepositoryDiscoveryServiceTests()
    {
        _api.GetCurrentUserRepositoriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<GitHubRepository>>([]));
        _api.GetRepositoryAsync(default!, default!, default).ReturnsForAnyArgs<GitHubRepository>(
            _ => throw new GitHubNotFoundException("not found", "repos"));
        _api.GetRepositoryByIdAsync(default, default).ReturnsForAnyArgs<GitHubRepository>(
            _ => throw new GitHubNotFoundException("not found", "repositories"));
        _service = new(_api, new ExclusionMatcher(), _logger);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DiscoverAsync_EmptyConfig_ReturnsEmpty()
    {
        var result = await _service.DiscoverAsync(new BackupConfig(), null, Token);

        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_OrganizationAccount_ListsThroughOrganizationEndpoint()
    {
        SetAccount("acme", GitHubAccountType.Organization, Repo(1, "acme", "tool"));

        var result = await _service.DiscoverAsync(Config(Account("acme")), null, Token);

        result.NewInAccounts.ShouldHaveSingleItem().Repository.Id.ShouldBe(1);
        await _api.DidNotReceiveWithAnyArgs().GetUserRepositoriesAsync(default!, Token);
    }

    [Fact]
    public async Task DiscoverAsync_UserAccount_ListsThroughUserEndpoint()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "tool"));

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewInAccounts.ShouldHaveSingleItem().Repository.Id.ShouldBe(1);
        await _api.DidNotReceiveWithAnyArgs().GetOrganizationRepositoriesAsync(default!, Token);
    }

    [Fact]
    public async Task DiscoverAsync_RepositoryInSeveralSources_IsReportedOnce()
    {
        var repository = Repo(1, "octo", "tool");
        SetCurrentUser(repository);
        SetAccount("octo", GitHubAccountType.User, repository);

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewInAccounts.Count.ShouldBe(1);
        result.NewStandalone.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_NewInAccount_ReportsAccountLoginAsConfigured()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "OCTO", "tool"));

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewInAccounts.ShouldHaveSingleItem().AccountLogin.ShouldBe("octo");
    }

    [Fact]
    public async Task DiscoverAsync_ForkAndArchivedAndPrivate_AreNewLikeAnyOther()
    {
        SetAccount(
            "octo",
            GitHubAccountType.User,
            Repo(1, "octo", "fork") with { IsFork = true },
            Repo(2, "octo", "old") with { IsArchived = true },
            Repo(3, "octo", "secret") with { IsPrivate = true });

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewInAccounts.Select(item => item.Repository.Id).ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public async Task DiscoverAsync_NewRepositories_AreOrderedByFullName()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "zeta"), Repo(2, "octo", "Alpha"), Repo(3, "octo", "beta"));

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewInAccounts.Select(item => item.Repository.Name).ShouldBe(["Alpha", "beta", "zeta"]);
    }

    [Fact]
    public async Task DiscoverAsync_UserRepositoryOutsideAccounts_IsNewStandalone()
    {
        SetCurrentUser(Repo(5, "friend", "shared"));
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Accessible(Config(Account("octo"))), null, Token);

        result.NewStandalone.ShouldHaveSingleItem().FullName.ShouldBe("friend/shared");
        result.NewInAccounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_StrictNoAccountsTwoStandalone_ReturnsNoNewDespiteOtherUserRepositories()
    {
        SetCurrentUser(Repo(5, "octo", "mine"), Repo(6, "acme", "lib"), Repo(2, "friend", "shared"), Repo(3, "friend", "other"));
        var config = Config();
        config.Repositories.Add(Standalone("friend", "shared", 2));
        config.Repositories.Add(Standalone("friend", "other", 3));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.HasChanges.ShouldBeFalse();
        result.NewStandalone.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_StrictOrganizationAndOtherOwnerRepositories_AreNotNew()
    {
        SetCurrentUser(Repo(5, "acme", "lib"), Repo(6, "friend", "shared"), Repo(7, "octo", "tool"));
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewStandalone.ShouldBeEmpty();
        result.NewInAccounts.ShouldHaveSingleItem().Repository.Id.ShouldBe(7);
    }

    [Fact]
    public async Task DiscoverAsync_StrictPrivateTrackedAccountRepositoryOnlyFromUserRepos_IsNew()
    {
        SetCurrentUser(Repo(5, "OCTO", "secret") with { IsPrivate = true });
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewInAccounts.ShouldHaveSingleItem().Repository.Id.ShouldBe(5);
    }

    [Fact]
    public async Task DiscoverAsync_StrictOtherOwnerWithInvalidName_LogsNoInvalidNameWarning()
    {
        SetCurrentUser(Repo(1, "friend", "con"), Repo(2, "nul", "shared"));
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.HasChanges.ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldNotContain(record => record.Id.Id == _invalidNameEventId);
    }

    [Fact]
    public async Task DiscoverAsync_AccessibleOtherOwnerRepository_IsNewStandalone()
    {
        SetCurrentUser(Repo(5, "acme", "lib"));
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Accessible(Config(Account("octo"))), null, Token);

        result.NewStandalone.ShouldHaveSingleItem().FullName.ShouldBe("acme/lib");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoverAsync_StandaloneRecordNotFound_IsMissing(bool accessible)
    {
        var config = Config();
        config.DiscoverAccessibleRepositories = accessible;
        config.Repositories.Add(Standalone("friend", "shared", 2));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.Missing.ShouldHaveSingleItem().Id.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoverAsync_StandaloneRecordInUserRepos_MatchedWithoutLookup(bool accessible)
    {
        SetCurrentUser(Repo(2, "friend", "shared"));
        var config = Config();
        config.DiscoverAccessibleRepositories = accessible;
        config.Repositories.Add(Standalone("friend", "shared", 2));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.HasChanges.ShouldBeFalse();
        await _api.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, Token);
        await _api.DidNotReceiveWithAnyArgs().GetRepositoryByIdAsync(default, Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoverAsync_StandaloneRecordRenamedInUserRepos_IsRenamed(bool accessible)
    {
        SetCurrentUser(Repo(2, "friend", "shared-v2"));
        var config = Config();
        config.DiscoverAccessibleRepositories = accessible;
        config.Repositories.Add(Standalone("friend", "shared", 2));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.Renamed.ShouldHaveSingleItem().Current.FullName.ShouldBe("friend/shared-v2");
        result.NewStandalone.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoverAsync_StandaloneRecordTransferredToOtherOwner_IsRenamedThroughIdLookup(bool accessible)
    {
        _api.GetRepositoryByIdAsync(2, Arg.Any<CancellationToken>()).Returns(Repo(2, "acme", "shared"));
        var config = Config();
        config.DiscoverAccessibleRepositories = accessible;
        config.Repositories.Add(Standalone("friend", "shared", 2));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.Renamed.ShouldHaveSingleItem().Current.FullName.ShouldBe("acme/shared");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoverAsync_UnavailableStandaloneRecordInUserRepos_IsReturned(bool accessible)
    {
        SetCurrentUser(Repo(2, "friend", "shared"));
        var config = Config();
        config.DiscoverAccessibleRepositories = accessible;
        var record = Standalone("friend", "shared", 2);
        record.Status = RepositoryStatus.Unavailable;
        config.Repositories.Add(record);

        var result = await _service.DiscoverAsync(config, null, Token);

        result.Returned.ShouldHaveSingleItem().Current.Id.ShouldBe(2);
    }

    [Fact]
    public async Task DiscoverAsync_UserRepositoryOfTrackedAccount_IsNewInAccount()
    {
        SetCurrentUser(Repo(5, "octo", "tool"));
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewInAccounts.ShouldHaveSingleItem().Repository.Id.ShouldBe(5);
        result.NewStandalone.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_KnownIdInAccountOrStandalone_IsNotNew()
    {
        SetCurrentUser(Repo(2, "friend", "shared"));
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "tool"));
        var config = Config(Account("octo", Active(1, "tool")));
        config.Repositories.Add(Standalone("friend", "shared", 2));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_ExcludedRepository_IsNeitherNewInAccountNorNewStandalone()
    {
        SetCurrentUser(Repo(5, "friend", "skip-me"));
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "skip-tool"));
        var config = Accessible(Config(Account("octo")));
        config.Exclude.Add("*/skip-*");

        var result = await _service.DiscoverAsync(config, null, Token);

        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_RecordExcludedUnderItsName_IsNotLookedUp()
    {
        var config = Config(Account("octo", Active(1, "skip-tool")));
        config.Exclude.Add("octo/skip-*");
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(config, null, Token);

        result.HasChanges.ShouldBeFalse();
        await _api.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, Token);
        await _api.DidNotReceiveWithAnyArgs().GetRepositoryByIdAsync(default, Token);
    }

    [Fact]
    public async Task DiscoverAsync_ActiveRecordNotFound_IsMissing()
    {
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        var missing = result.Missing.ShouldHaveSingleItem();
        missing.FullName.ShouldBe("octo/tool");
        missing.AccountLogin.ShouldBe("octo");
    }

    [Fact]
    public async Task DiscoverAsync_ActiveRecordAccessDenied_IsMissing()
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns<GitHubRepository>(
            _ => throw new GitHubAccessDeniedException("denied", "repos/octo/tool"));
        _api.GetRepositoryByIdAsync(1, Arg.Any<CancellationToken>()).Returns<GitHubRepository>(
            _ => throw new GitHubAccessDeniedException("denied", "repositories/1"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.Missing.ShouldHaveSingleItem().Id.ShouldBe(1);
    }

    [Fact]
    public async Task DiscoverAsync_NameTakenByOtherIdAndIdNotFound_IsMissing()
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns(Repo(99, "octo", "tool"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.Missing.ShouldHaveSingleItem().Id.ShouldBe(1);
    }

    [Fact]
    public async Task DiscoverAsync_StandaloneRecordNotFound_IsMissingWithoutAccount()
    {
        var config = Config();
        config.Repositories.Add(Standalone("friend", "shared", 2));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.Missing.ShouldHaveSingleItem().AccountLogin.ShouldBeNull();
    }

    [Fact]
    public async Task DiscoverAsync_ActiveRecordReachableOutsideLists_HasNoEvents()
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns(Repo(1, "octo", "tool"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_ListedAndLookedUpRepositories_AreInRepositoriesById()
    {
        var listed = Repo(2, "octo", "listed");
        var lookedUp = Repo(1, "octo", "tool");
        SetCurrentUser(Repo(3, "friend", "shared"));
        SetAccount("octo", GitHubAccountType.User, listed);
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns(lookedUp);

        var result = await _service.DiscoverAsync(Accessible(Config(Account("octo", Active(1, "tool"), Active(2, "listed")))), null, Token);

        result.Repositories.Keys.Order().ShouldBe([1L, 2L, 3L]);
        result.Repositories[1].ShouldBe(lookedUp);
        result.Repositories[2].ShouldBe(listed);
        result.HasChanges.ShouldBeTrue();
    }

    [Fact]
    public async Task DiscoverAsync_RecordNotFound_IsNotInRepositories()
    {
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.Repositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_UnavailableRecordStillNotFound_HasNoEvents()
    {
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Config(Account("octo", Unavailable(1, "tool"))), null, Token);

        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_UnavailableRecordFoundInList_IsReturned()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "tool"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Unavailable(1, "tool"))), null, Token);

        var returned = result.Returned.ShouldHaveSingleItem();
        returned.Tracked.FullName.ShouldBe("octo/tool");
        returned.Current.Id.ShouldBe(1);
        result.Renamed.ShouldBeEmpty();
        result.NewInAccounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_UnavailableRecordFoundByLookup_IsReturned()
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns(Repo(1, "octo", "tool"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Unavailable(1, "tool"))), null, Token);

        result.Returned.ShouldHaveSingleItem().Current.FullName.ShouldBe("octo/tool");
    }

    [Fact]
    public async Task DiscoverAsync_RecordRenamedInList_IsRenamedAndNotNew()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "tool-v2"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        var renamed = result.Renamed.ShouldHaveSingleItem();
        renamed.Tracked.FullName.ShouldBe("octo/tool");
        renamed.Current.FullName.ShouldBe("octo/tool-v2");
        result.NewInAccounts.ShouldBeEmpty();
        result.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_RecordTransferredToOtherOwner_IsRenamedThroughIdLookup()
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Repo(1, "friend", "tool"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.Renamed.ShouldHaveSingleItem().Current.FullName.ShouldBe("friend/tool");
    }

    [Fact]
    public async Task DiscoverAsync_RecordRedirected_IsRenamed()
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns(Repo(1, "octo", "tool-v2"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.Renamed.ShouldHaveSingleItem().Current.FullName.ShouldBe("octo/tool-v2");
    }

    [Fact]
    public async Task DiscoverAsync_NameTakenByOtherId_FindsRenameById()
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns(Repo(99, "octo", "tool"));
        _api.GetRepositoryByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Repo(1, "octo", "tool-old"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.Renamed.ShouldHaveSingleItem().Current.FullName.ShouldBe("octo/tool-old");
    }

    [Fact]
    public async Task DiscoverAsync_NameDiffersOnlyInCase_IsNotRenamed()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "Tool"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_UnavailableRecordRenamed_IsReturnedAndRenamed()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "tool-v2"));

        var result = await _service.DiscoverAsync(Config(Account("octo", Unavailable(1, "tool"))), null, Token);

        result.Returned.ShouldHaveSingleItem();
        result.Renamed.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DiscoverAsync_RecordRenamedToExcludedName_IsStillRenamed()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "skip-tool"));
        var config = Config(Account("octo", Active(1, "tool")));
        config.Exclude.Add("octo/skip-*");

        var result = await _service.DiscoverAsync(config, null, Token);

        result.Renamed.ShouldHaveSingleItem().Current.Name.ShouldBe("skip-tool");
        result.NewInAccounts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("con")]
    [InlineData("x.wiki")]
    [InlineData("name.")]
    public async Task DiscoverAsync_NewAccountRepositoryWithInvalidName_IsNotNew(string name)
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", name), Repo(2, "octo", "tool"));

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewInAccounts.ShouldHaveSingleItem().Repository.Id.ShouldBe(2);
        result.NewStandalone.ShouldBeEmpty();
        ShouldLogInvalidName($"octo/{name}");
    }

    [Theory]
    [InlineData("friend", "con")]
    [InlineData("friend", "x.wiki")]
    [InlineData("friend", "name.")]
    [InlineData("nul", "shared")]
    public async Task DiscoverAsync_NewStandaloneRepositoryWithInvalidName_IsNotNew(string owner, string name)
    {
        SetCurrentUser(Repo(1, owner, name), Repo(2, "friend", "shared"));
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Accessible(Config(Account("octo"))), null, Token);

        result.NewStandalone.ShouldHaveSingleItem().Id.ShouldBe(2);
        result.NewInAccounts.ShouldBeEmpty();
        ShouldLogInvalidName($"{owner}/{name}");
    }

    [Theory]
    [InlineData("con")]
    [InlineData("x.wiki")]
    [InlineData("name.")]
    public async Task DiscoverAsync_RecordRenamedToInvalidNameInList_IsNeitherRenamedNorMissing(string name)
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", name));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.HasChanges.ShouldBeFalse();
        result.Repositories.ShouldContainKey(1);
        ShouldLogInvalidName($"octo/{name}");
    }

    [Theory]
    [InlineData("octo", "con")]
    [InlineData("nul", "tool")]
    public async Task DiscoverAsync_RecordRenamedToInvalidNameByIdLookup_IsNeitherRenamedNorMissing(string owner, string name)
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Repo(1, owner, name));

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token);

        result.HasChanges.ShouldBeFalse();
        ShouldLogInvalidName($"{owner}/{name}");
    }

    [Fact]
    public async Task DiscoverAsync_ExcludedRepositoryWithInvalidName_LogsNoInvalidNameWarning()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "con"));
        var config = Config(Account("octo"));
        config.Exclude.Add("octo/*");

        var result = await _service.DiscoverAsync(config, null, Token);

        result.HasChanges.ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldNotContain(record => record.Id.Id == _invalidNameEventId);
    }

    [Fact]
    public async Task DiscoverAsync_RecordsOfSeveralAccounts_KeepFileOrder()
    {
        SetAccount("zed", GitHubAccountType.User);
        SetAccount("abe", GitHubAccountType.User);
        var config = Config(Account("zed", Active(1, "b"), Active(2, "a")), Account("abe", Active(3, "c")));
        config.Repositories.Add(Standalone("friend", "s", 4));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.Missing.Select(item => item.Id).ShouldBe([1L, 2L, 3L, 4L]);
    }

    [Fact]
    public async Task DiscoverAsync_InvalidAccountUrl_ThrowsArgumentException()
    {
        var config = Config(new AccountConfig { Url = "https://example.com/octo" });

        await Should.ThrowAsync<ArgumentException>(() => _service.DiscoverAsync(config, null, Token));
    }

    [Fact]
    public async Task DiscoverAsync_InvalidRepositoryUrl_ThrowsArgumentException()
    {
        var config = Config();
        config.Repositories.Add(new RepositoryConfig { Url = "https://github.com/octo", Id = 1 });

        await Should.ThrowAsync<ArgumentException>(() => _service.DiscoverAsync(config, null, Token));
    }

    [Fact]
    public async Task DiscoverAsync_UserListingRateLimited_Propagates()
    {
        _api.GetCurrentUserRepositoriesAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<GitHubRepository>>(
            _ => throw RateLimit());

        await Should.ThrowAsync<GitHubRateLimitExceededException>(
            () => _service.DiscoverAsync(Config(Account("octo")), null, Token));
    }

    [Fact]
    public async Task DiscoverAsync_AuthenticationFailure_Propagates()
    {
        _api.GetCurrentUserRepositoriesAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<GitHubRepository>>(
            _ => throw new GitHubAuthenticationException("bad token", "user/repos"));

        await Should.ThrowAsync<GitHubAuthenticationException>(
            () => _service.DiscoverAsync(Config(Account("octo")), null, Token));
    }

    [Fact]
    public async Task DiscoverAsync_AccountNotFound_Propagates()
    {
        _api.GetAccountAsync("octo", Arg.Any<CancellationToken>()).Returns<GitHubAccount>(
            _ => throw new GitHubNotFoundException("not found", "users/octo"));

        await Should.ThrowAsync<GitHubNotFoundException>(
            () => _service.DiscoverAsync(Config(Account("octo")), null, Token));
    }

    [Fact]
    public async Task DiscoverAsync_RateLimitedWhileCheckingRecord_Propagates()
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns<GitHubRepository>(
            _ => throw RateLimit());

        await Should.ThrowAsync<GitHubRateLimitExceededException>(
            () => _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"))), null, Token));
    }

    [Fact]
    public async Task DiscoverAsync_AccountFilter_ListsOnlyThatAccountIgnoringCase()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(1, "octo", "tool"));
        SetAccount("acme", GitHubAccountType.Organization, Repo(2, "acme", "lib"));

        var result = await _service.DiscoverAsync(Config(Account("octo"), Account("acme")), "OCTO", Token);

        result.NewInAccounts.ShouldHaveSingleItem().Repository.Id.ShouldBe(1);
        await _api.DidNotReceive().GetAccountAsync("acme", Arg.Any<CancellationToken>());
        await _api.DidNotReceiveWithAnyArgs().GetOrganizationRepositoriesAsync(default!, Token);
    }

    [Fact]
    public async Task DiscoverAsync_AccountFilter_SkipsNewStandaloneAndOtherAccounts()
    {
        SetCurrentUser(Repo(5, "friend", "shared"), Repo(6, "acme", "lib"));
        SetAccount("octo", GitHubAccountType.User);

        var result = await _service.DiscoverAsync(Accessible(Config(Account("octo"), Account("acme"))), "octo", Token);

        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_AccountFilter_ChecksOnlyRecordsOfThatAccount()
    {
        SetAccount("octo", GitHubAccountType.User);
        var config = Config(Account("octo", Active(1, "tool")), Account("acme", Active(2, "lib")));
        config.Repositories.Add(Standalone("friend", "shared", 3));

        var result = await _service.DiscoverAsync(config, "octo", Token);

        result.Missing.ShouldHaveSingleItem().Id.ShouldBe(1);
    }

    [Fact]
    public async Task DiscoverAsync_AccountFilter_KnownIdsComeFromWholeConfig()
    {
        SetCurrentUser(Repo(3, "octo", "moved"));
        SetAccount("octo", GitHubAccountType.User);
        var config = Config(Account("octo"));
        config.Repositories.Add(Standalone("octo", "moved", 3));

        var result = await _service.DiscoverAsync(config, "octo", Token);

        result.NewInAccounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_UnknownAccountFilter_ThrowsBeforeAnyRequest()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => _service.DiscoverAsync(Config(Account("octo")), "nobody", Token));

        await _api.ReceivedWithAnyArgs(0).GetCurrentUserRepositoriesAsync(Token);
        await _api.ReceivedWithAnyArgs(0).GetAccountAsync(default!, Token);
    }

    [Fact]
    public async Task DiscoverAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _service.DiscoverAsync(Config(Account("octo")), null, cancellation.Token));
    }

    [Fact]
    public async Task ResolveIdsAsync_StandaloneWithoutId_ReturnsIdFromNameAndLogsIt()
    {
        var config = new BackupConfig { Repositories = [Standalone("someone", "lib", 0)] };
        var repository = Repo(42, "someone", "lib");
        _api.GetRepositoryAsync("someone", "lib", Arg.Any<CancellationToken>()).Returns(repository);

        var result = await _service.ResolveIdsAsync(config, Items(config), Token);

        result.Resolved.ShouldBe([new ResolvedRepositoryId("someone", "lib", null, repository)]);
        result.NotFound.ShouldBeEmpty();
        result.Conflicts.ShouldBeEmpty();
        config.Repositories[0].Id.ShouldBe(0);
        var record = _logger.Collector.GetSnapshot().Where(r => r.Id.Id == _idResolvedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Repository").ShouldBe("someone/lib");
        record.GetStructuredStateValue("Id").ShouldBe("42");
    }

    [Fact]
    public async Task ResolveIdsAsync_AccountEntryWithoutId_ReturnsIdWithAccountLogin()
    {
        var config = Config(Account("octo", Active(0, "tool")));
        var repository = Repo(7, "octo", "tool");
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns(repository);

        var result = await _service.ResolveIdsAsync(config, Items(config), Token);

        result.Resolved.ShouldBe([new ResolvedRepositoryId("octo", "tool", "octo", repository)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveIdsAsync_NotFound_ReturnsNotFoundAndLogsWarning(bool accessDenied)
    {
        var config = Config(Account("octo", Active(0, "gone")));
        _api.GetRepositoryAsync("octo", "gone", Arg.Any<CancellationToken>()).Returns<GitHubRepository>(_ =>
            throw (accessDenied
                ? new GitHubAccessDeniedException("denied", "repos")
                : new GitHubNotFoundException("not found", "repos")));

        var result = await _service.ResolveIdsAsync(config, Items(config), Token);

        result.NotFound.ShouldBe(["octo/gone"]);
        result.Resolved.ShouldBeEmpty();
        var record = _logger.Collector.GetSnapshot().Where(r => r.Id.Id == _idNotFoundEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Repository").ShouldBe("octo/gone");
    }

    [Fact]
    public async Task ResolveIdsAsync_RateLimit_Throws()
    {
        var config = Config(Account("octo", Active(0, "tool")));
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns<GitHubRepository>(_ => throw RateLimit());

        await Should.ThrowAsync<GitHubRateLimitExceededException>(() => _service.ResolveIdsAsync(config, Items(config), Token));
    }

    [Fact]
    public async Task ResolveIdsAsync_NotFound_NeverLooksUpById()
    {
        var config = Config(Account("octo", Active(0, "gone")));

        await _service.ResolveIdsAsync(config, Items(config), Token);

        await _api.DidNotReceiveWithAnyArgs().GetRepositoryByIdAsync(default, Token);
    }

    [Fact]
    public async Task ResolveIdsAsync_EntryWithId_NotLookedUp()
    {
        var config = Config(Account("octo", Active(5, "tool")));

        var result = await _service.ResolveIdsAsync(config, Items(config), Token);

        result.Resolved.ShouldBeEmpty();
        result.NotFound.ShouldBeEmpty();
        result.Conflicts.ShouldBeEmpty();
        await _api.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, Token);
    }

    [Fact]
    public async Task ResolveIdsAsync_IdOfOtherEntry_ReturnsConflictNamingThatEntry()
    {
        var config = Config(Account("octo", Active(7, "old-name"), Active(0, "new-name")));
        _api.GetRepositoryAsync("octo", "new-name", Arg.Any<CancellationToken>()).Returns(Repo(7, "octo", "new-name"));

        var result = await _service.ResolveIdsAsync(config, Items(config), Token);

        result.Resolved.ShouldBeEmpty();
        result.Conflicts.ShouldBe(
        [
            new RepositoryIdConflict(
                "octo/new-name",
                "The id 7 found on GitHub for octo/new-name already belongs to octo/old-name in backup-config.json."),
        ]);
        var record = _logger.Collector.GetSnapshot().Where(r => r.Id.Id == _idConflictEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("OtherRepository").ShouldBe("octo/old-name");
    }

    [Fact]
    public async Task ResolveIdsAsync_TwoEntriesSameId_SecondConflicts()
    {
        var config = Config(Account("octo", Active(0, "old-name"), Active(0, "new-name")));
        var repository = Repo(7, "octo", "new-name");
        _api.GetRepositoryAsync("octo", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(repository);

        var result = await _service.ResolveIdsAsync(config, Items(config), Token);

        result.Resolved.ShouldBe([new ResolvedRepositoryId("octo", "old-name", "octo", repository)]);
        result.Conflicts.ShouldHaveSingleItem().FullName.ShouldBe("octo/new-name");
        result.Conflicts[0].Reason.ShouldContain("already belongs to octo/old-name");
    }

    [Fact]
    public async Task ResolveIdsAsync_Cancelled_Throws()
    {
        var config = Config(Account("octo", Active(0, "tool")));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _service.ResolveIdsAsync(config, Items(config), cancellation.Token));
        await _api.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, Token);
    }

    [Fact]
    public async Task DiscoverAsync_EntryWithoutId_NotMissingNotRenamedNoLookup()
    {
        SetAccount("octo", GitHubAccountType.User);
        var config = Config(Account("octo", Active(0, "tool")));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.HasChanges.ShouldBeFalse();
        await _api.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, Token);
        await _api.DidNotReceiveWithAnyArgs().GetRepositoryByIdAsync(default, Token);
    }

    [Fact]
    public async Task DiscoverAsync_CollectedRepositoryNamedLikeEntryWithoutId_NotNew()
    {
        SetAccount("octo", GitHubAccountType.User, Repo(7, "OCTO", "Tool"));
        SetCurrentUser(Repo(8, "someone", "lib"));
        var config = Config(Account("octo", Active(0, "tool")));
        config.Repositories.Add(Standalone("someone", "lib", 0));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_ResolvedEntryFoundByName_NoRename()
    {
        SetAccount("octo", GitHubAccountType.User);
        _api.GetRepositoryAsync("octo", "tool", Arg.Any<CancellationToken>()).Returns(Repo(7, "octo", "tool"));
        var config = Config(Account("octo", Active(7, "tool")));

        var result = await _service.DiscoverAsync(config, null, Token);

        result.HasChanges.ShouldBeFalse();
        await _api.DidNotReceiveWithAnyArgs().GetRepositoryByIdAsync(default, Token);
    }

    /// <summary>
    /// Lists the entries of <paramref name="config"/> as work items, with an archive root set so the folders resolve.
    /// </summary>
    /// <param name="config">The configuration, whose backup root is set when it is empty.</param>
    /// <returns>The work items.</returns>
    private static IReadOnlyList<BackupWorkItem> Items(BackupConfig config)
    {
        if (config.BackupRoot.Length == 0)
        {
            config.BackupRoot = @"C:\backup";
        }

        return BackupWorkItem.CreateAll(config, new RepositoryPathResolver(@"C:\app\sources"));
    }

    /// <summary>
    /// Asserts that discovery logged exactly one invalid-name warning, for <paramref name="repository"/>.
    /// </summary>
    /// <param name="repository">The expected <c>owner/name</c>.</param>
    private void ShouldLogInvalidName(string repository)
    {
        var record = _logger.Collector.GetSnapshot().Where(r => r.Id.Id == _invalidNameEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Repository").ShouldBe(repository);
    }

    private static GitHubRepository Repo(long id, string owner, string name) =>
        new(id, owner, name, false, false, false, true, "main", $"https://github.com/{owner}/{name}");

    private static GitHubRateLimitExceededException RateLimit() =>
        new("rate limit", "repos", 403, null, 60, 0);

    private static BackupConfig Config(params AccountConfig[] accounts) => new() { Accounts = [.. accounts] };

    private static BackupConfig Accessible(BackupConfig config)
    {
        config.DiscoverAccessibleRepositories = true;
        return config;
    }

    private static AccountConfig Account(string login, params AccountRepositoryConfig[] repositories) =>
        new() { Url = $"https://github.com/{login}", Repositories = [.. repositories] };

    private static AccountRepositoryConfig Active(long id, string name) =>
        new() { Id = id, Name = name, Status = RepositoryStatus.Active };

    private static AccountRepositoryConfig Unavailable(long id, string name) =>
        new() { Id = id, Name = name, Status = RepositoryStatus.Unavailable };

    private static RepositoryConfig Standalone(string owner, string name, long id) =>
        new() { Url = $"https://github.com/{owner}/{name}", Id = id, Status = RepositoryStatus.Active };

    private void SetCurrentUser(params GitHubRepository[] repositories) =>
        _api.GetCurrentUserRepositoriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<GitHubRepository>>(repositories));

    private void SetAccount(string login, GitHubAccountType type, params GitHubRepository[] repositories)
    {
        _api.GetAccountAsync(login, Arg.Any<CancellationToken>()).Returns(new GitHubAccount(login, type));
        var list = Task.FromResult<IReadOnlyList<GitHubRepository>>(repositories);
        if (type == GitHubAccountType.Organization)
        {
            _api.GetOrganizationRepositoriesAsync(login, Arg.Any<CancellationToken>()).Returns(list);
        }
        else
        {
            _api.GetUserRepositoriesAsync(login, Arg.Any<CancellationToken>()).Returns(list);
        }
    }
}
