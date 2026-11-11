using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GitHubBackup.Core.Tests.Discovery;

/// <summary>
/// Tests <see cref="RepositoryDiscoveryService"/>.
/// </summary>
public sealed class RepositoryDiscoveryServiceTests
{
    private readonly IGitHubApi _api = Substitute.For<IGitHubApi>();
    private readonly RepositoryDiscoveryService _service;

    public RepositoryDiscoveryServiceTests()
    {
        _api.GetCurrentUserRepositoriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<GitHubRepository>>([]));
        _api.GetRepositoryAsync(default!, default!, default).ReturnsForAnyArgs<GitHubRepository>(
            _ => throw new GitHubNotFoundException("not found", "repos"));
        _api.GetRepositoryByIdAsync(default, default).ReturnsForAnyArgs<GitHubRepository>(
            _ => throw new GitHubNotFoundException("not found", "repositories"));
        _service = new(_api, new ExclusionMatcher(), NullLogger<RepositoryDiscoveryService>.Instance);
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

        var result = await _service.DiscoverAsync(Config(Account("octo")), null, Token);

        result.NewStandalone.ShouldHaveSingleItem().FullName.ShouldBe("friend/shared");
        result.NewInAccounts.ShouldBeEmpty();
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
        var config = Config(Account("octo"));
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

        var result = await _service.DiscoverAsync(Config(Account("octo", Active(1, "tool"), Active(2, "listed"))), null, Token);

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

        var result = await _service.DiscoverAsync(Config(Account("octo"), Account("acme")), "octo", Token);

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

    private static GitHubRepository Repo(long id, string owner, string name) =>
        new(id, owner, name, false, false, false, true, "main", $"https://github.com/{owner}/{name}");

    private static GitHubRateLimitExceededException RateLimit() =>
        new("rate limit", "repos", 403, null, 60, 0);

    private static BackupConfig Config(params AccountConfig[] accounts) => new() { Accounts = [.. accounts] };

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
