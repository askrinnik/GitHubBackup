using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.GitHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace GitHubBackup.Infrastructure.Tests.GitHub;

/// <summary>
/// Contract tests of <see cref="OctokitGitHubApi"/> against a WireMock.Net server on the loopback interface.
/// </summary>
public sealed class OctokitGitHubApiTests : IDisposable
{
    private const string _token = "ghp_0123456789abcdefghijklmnopqrstuvwxyzAB";
    private const long _resetEpochSeconds = 1_893_456_000;
    private const int _rateLimitEventId = 1;
    private const int _currentUserRepositoriesSkippedEventId = 2;
    private const int _rateLimitLowEventId = 3;
    private const int _requestingEventId = 4;
    private const int _requestCompletedEventId = 5;

    private static readonly DateTimeOffset _now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _resetAt = DateTimeOffset.FromUnixTimeSeconds(_resetEpochSeconds);

    private const string _otherToken = "ghp_ZYXWVUTSRQPONMLKJIHGFEDCBA9876543210zy";

    private static readonly GitHubToken _configuredToken = new(_token, "test");

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly FakeLogger<OctokitGitHubApi> _logger = new();
    private readonly FakeTimeProvider _timeProvider = new(_now);

    /// <summary>
    /// The token that <see cref="CreateApi"/> gives the subject.
    /// </summary>
    private GitHubToken _runToken = _configuredToken;

    /// <summary>
    /// The scenarios of <see cref="GetRepositoryAsync_Found_MapsFlags"/>: the JSON flags and the expected record.
    /// </summary>
    public static TheoryData<bool, bool, bool, bool> RepositoryFlags => new()
    {
        { false, false, false, false },
        { true, false, false, true },
        { false, true, false, true },
        { false, false, true, false },
        { true, true, true, true },
    };

    /// <summary>
    /// The calls that take a login, owner or name, each with an invalid value.
    /// </summary>
    public static TheoryData<string> InvalidInputCalls => ["account", "organization", "user", "owner", "name", "id"];

    /// <summary>
    /// Every API call with the path it requests, the description it logs and the resource of its completion entry.
    /// </summary>
    public static TheoryData<string, string, string, string> DescribedCalls => new()
    {
        { "account", "/users/octocat", "Requesting the account of an owner", "users/octocat" },
        { "current", "/user/repos", "Requesting the repositories of the authenticated user", "user/repos" },
        { "organization", "/orgs/acme/repos", "Requesting the repositories of an organization", "orgs/acme/repos" },
        { "user", "/users/octocat/repos", "Requesting the repositories of a user", "users/octocat/repos" },
        { "repository", "/repos/octocat/repo", "Requesting a repository by owner and name", "repos/octocat/repo" },
        { "id", "/repositories/42", "Requesting a repository by its id", "repositories/42" },
    };

    /// <inheritdoc />
    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task GetCurrentUserRepositoriesAsync_TwoPages_ReturnsAllRepositories()
    {
        GivenTwoPages("/user/repos", "owner");

        var repositories = await CreateApi().GetCurrentUserRepositoriesAsync(TestContext.Current.CancellationToken);

        repositories.Select(repository => repository.FullName).ShouldBe(["owner/first", "owner/second", "owner/third"]);
    }

    [Fact]
    public async Task GetCurrentUserRepositoriesAsync_Request_SendsAffiliationAndVisibility()
    {
        _server
            .Given(Request.Create()
                .WithPath("/user/repos")
                .WithParam("affiliation", "owner,collaborator,organization_member")
                .WithParam("visibility", "all")
                .WithParam("per_page", "100")
                .UsingGet())
            .RespondWith(Json(RepositoriesJson(Repository(1, "owner", "first"))));

        var repositories = await CreateApi().GetCurrentUserRepositoriesAsync(TestContext.Current.CancellationToken);

        repositories.ShouldHaveSingleItem().Name.ShouldBe("first");
        QueryOfOnlyRequest().ShouldBe(
            ["affiliation=owner,collaborator,organization_member", "per_page=100", "visibility=all"]);
    }

    [Fact]
    public async Task GetOrganizationRepositoriesAsync_TwoPages_ReturnsAllRepositoriesWithTypeAll()
    {
        GivenTwoPages("/orgs/acme/repos", "acme", "type");

        var repositories = await CreateApi().GetOrganizationRepositoriesAsync("acme", TestContext.Current.CancellationToken);

        repositories.Select(repository => repository.FullName).ShouldBe(["acme/first", "acme/second", "acme/third"]);
        _server.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_TwoPages_ReturnsAllRepositoriesWithTypeAll()
    {
        GivenTwoPages("/users/octocat/repos", "octocat", "type");

        var repositories = await CreateApi().GetUserRepositoriesAsync("octocat", TestContext.Current.CancellationToken);

        repositories.Select(repository => repository.FullName).ShouldBe(["octocat/first", "octocat/second", "octocat/third"]);
        _server.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_NextPageOnForeignHost_ThrowsWithoutSendingToken()
    {
        using var foreignServer = WireMockServer.Start();
        foreignServer
            .Given(Request.Create().UsingAnyMethod())
            .RespondWith(Json(RepositoriesJson()));
        GivenFirstPageLinkingTo("/users/octocat/repos", $"{foreignServer.Url}/users/octocat/repos?page=2");

        var exception = await Should.ThrowAsync<GitHubApiException>(
            () => CreateApi().GetUserRepositoriesAsync("octocat", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("outside the configured API address");
        foreignServer.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_NextPageOutsideApiPath_ThrowsWithoutRequestingIt()
    {
        GivenFirstPageLinkingTo("/api/v3/users/octocat/repos", $"{_server.Url}/elsewhere/users/octocat/repos?page=2");

        await Should.ThrowAsync<GitHubApiException>(
            () => CreateApi($"{_server.Url}/api/v3").GetUserRepositoriesAsync("octocat", TestContext.Current.CancellationToken));

        _server.LogEntries.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetCurrentUserRepositoriesAsync_Anonymous_ReturnsEmptyWithoutRequest()
    {
        _runToken = GitHubToken.Anonymous;

        var repositories = await CreateApi().GetCurrentUserRepositoriesAsync(TestContext.Current.CancellationToken);

        repositories.ShouldBeEmpty();
        _server.LogEntries.ShouldBeEmpty();
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_currentUserRepositoriesSkippedEventId);
        record.Level.ShouldBe(LogLevel.Debug);
    }

    [Theory]
    [MemberData(nameof(RepositoryFlags))]
    public async Task GetRepositoryAsync_Found_MapsFlags(bool isPrivate, bool isFork, bool isArchived, bool hasWiki)
    {
        GivenRepository("/repos/owner/repo", Repository(42, "Owner", "Repo", isPrivate, isFork, isArchived, hasWiki));

        var repository = await CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        repository.ShouldBe(new(42, "Owner", "Repo", isPrivate, isFork, isArchived, hasWiki, "develop", "https://github.com/Owner/Repo"));
    }

    [Fact]
    public async Task GetRepositoryByIdAsync_Found_ReturnsRepository()
    {
        GivenRepository("/repositories/42", Repository(42, "owner", "renamed"));

        var repository = await CreateApi().GetRepositoryByIdAsync(42, TestContext.Current.CancellationToken);

        repository.Id.ShouldBe(42);
        repository.FullName.ShouldBe("owner/renamed");
    }

    [Fact]
    public async Task GetAccountAsync_TypeOrganization_ReturnsOrganization()
    {
        GivenAccount("acme", "Acme", "Organization");

        var account = await CreateApi().GetAccountAsync("acme", TestContext.Current.CancellationToken);

        account.ShouldBe(new("Acme", GitHubAccountType.Organization));
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Bot")]
    public async Task GetAccountAsync_TypeUser_ReturnsUser(string type)
    {
        GivenAccount("octocat", "OctoCat", type);

        var account = await CreateApi().GetAccountAsync("octocat", TestContext.Current.CancellationToken);

        account.ShouldBe(new("OctoCat", GitHubAccountType.User));
    }

    [Fact]
    public async Task GetRepositoryAsync_Unauthorized_ThrowsGitHubAuthenticationException()
    {
        GivenError("/repos/owner/repo", 401, "Bad credentials");

        var exception = await Should.ThrowAsync<GitHubAuthenticationException>(
            () => CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(401);
        exception.Resource.ShouldBe("repos/owner/repo");
        exception.Message.ShouldContain("Bad credentials");
        exception.Message.ShouldNotContain(_token);
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task GetRepositoryAsync_Forbidden_ThrowsGitHubAccessDeniedException()
    {
        GivenError("/repos/owner/repo", 403, "Resource not accessible by personal access token");

        var exception = await Should.ThrowAsync<GitHubAccessDeniedException>(
            () => CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(403);
        exception.Message.ShouldContain("repos/owner/repo");
    }

    [Fact]
    public async Task GetRepositoryAsync_NotFound_ThrowsGitHubNotFoundException()
    {
        GivenError("/repos/owner/repo", 404, "Not Found");

        var exception = await Should.ThrowAsync<GitHubNotFoundException>(
            () => CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(404);
        exception.Resource.ShouldBe("repos/owner/repo");
    }

    [Fact]
    public async Task GetAccountAsync_NotFound_ThrowsGitHubNotFoundException()
    {
        GivenError("/users/ghost", 404, "Not Found");

        var exception = await Should.ThrowAsync<GitHubNotFoundException>(
            () => CreateApi().GetAccountAsync("ghost", TestContext.Current.CancellationToken));

        exception.Resource.ShouldBe("users/ghost");
    }

    [Fact]
    public async Task GetCurrentUserRepositoriesAsync_RateLimitExhausted_ThrowsWithResetTime()
    {
        _server
            .Given(Request.Create().WithPath("/user/repos").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(403)
                .WithHeader("Content-Type", "application/json")
                .WithHeader("X-RateLimit-Limit", "5000")
                .WithHeader("X-RateLimit-Remaining", "0")
                .WithHeader("X-RateLimit-Reset", _resetEpochSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .WithBody(ErrorJson("API rate limit exceeded for user ID 1.")));

        var exception = await Should.ThrowAsync<GitHubRateLimitExceededException>(
            () => CreateApi().GetCurrentUserRepositoriesAsync(TestContext.Current.CancellationToken));

        exception.ResetAt.ShouldBe(_resetAt);
        exception.Limit.ShouldBe(5000);
        exception.Remaining.ShouldBe(0);
        exception.StatusCode.ShouldBe(403);
        exception.Message.ShouldContain("2030-01-01T00:00:00.0000000+00:00");
    }

    [Theory]
    [InlineData(403, "You have exceeded a secondary rate limit. Please wait a few minutes before you try again.")]
    [InlineData(429, "Too many requests.")]
    public async Task GetRepositoryAsync_SecondaryRateLimitWithRetryAfter_ThrowsWithResetTime(int statusCode, string message)
    {
        _server
            .Given(Request.Create().WithPath("/repos/owner/repo").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithHeader("Retry-After", "60")
                .WithBody(ErrorJson(message)));

        var exception = await Should.ThrowAsync<GitHubRateLimitExceededException>(
            () => CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken));

        exception.ResetAt.ShouldBe(_now.AddSeconds(60));
        exception.StatusCode.ShouldBe(statusCode);
    }

    [Fact]
    public async Task GetRepositoryAsync_SecondaryRateLimitWithoutRetryAfter_ThrowsWithoutResetTime()
    {
        GivenError("/repos/owner/repo", 403, "You have exceeded a secondary rate limit.");

        var exception = await Should.ThrowAsync<GitHubRateLimitExceededException>(
            () => CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken));

        exception.ResetAt.ShouldBeNull();
        exception.Message.ShouldContain("did not report");
    }

    [Fact]
    public async Task GetRepositoryAsync_ServerError_ThrowsGitHubApiException()
    {
        GivenError("/repos/owner/repo", 502, "Server Error");

        var exception = await Should.ThrowAsync<GitHubApiException>(
            () => CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken));

        exception.ShouldBeOfType<GitHubApiException>();
        exception.StatusCode.ShouldBe(502);
        exception.Resource.ShouldBe("repos/owner/repo");
    }

    [Fact]
    public async Task GetRepositoryAsync_Success_LogsRemainingRateLimitAtDebug()
    {
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));

        await CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        var record = _logger.Collector.GetSnapshot().Where(r => r.Id.Id == _rateLimitEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Debug);
        record.GetStructuredStateValue("Remaining").ShouldBe("4999");
        record.GetStructuredStateValue("Limit").ShouldBe("5000");
    }

    [Theory]
    [MemberData(nameof(DescribedCalls))]
    public async Task AnyMethod_Success_LogsDescriptionThenCompletionAtDebug(
        string call, string path, string description, string resource)
    {
        GivenCallResponse(path);
        var api = CreateApi();
        _timeProvider.AutoAdvanceAmount = TimeSpan.FromMilliseconds(250);

        await InvokeAsync(api, call);

        var records = _logger.Collector.GetSnapshot();
        records.Select(record => record.Id.Id).ShouldBe([_requestingEventId, _requestCompletedEventId, _rateLimitEventId]);
        records[0].Level.ShouldBe(LogLevel.Information);
        records[0].GetStructuredStateValue("Description").ShouldBe(description);
        records[1].Level.ShouldBe(LogLevel.Debug);
        records[1].GetStructuredStateValue("Resource").ShouldBe(resource);
        records[1].GetStructuredStateValue("Duration").ShouldBe(TimeSpan.FromMilliseconds(250).ToString());
    }

    [Theory]
    [MemberData(nameof(DescribedCalls))]
    public async Task AnyMethod_Description_NamesNoLoginOrRepository(
        string call, string path, string description, string resource)
    {
        GivenCallResponse(path);

        await InvokeAsync(CreateApi(), call);

        var logged = _logger.Collector.GetSnapshot().First(r => r.Id.Id == _requestingEventId);
        logged.GetStructuredStateValue("Description").ShouldBe(description);
        description.ShouldNotContain("octocat", Case.Insensitive);
        description.ShouldNotContain("acme", Case.Insensitive);
        description.ShouldNotContain("repo/", Case.Insensitive);
        description.ShouldNotContain("42");
        resource.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("current", "/user/repos", null)]
    [InlineData("organization", "/orgs/acme/repos", "type")]
    [InlineData("user", "/users/octocat/repos", "type")]
    public async Task RepositoryList_TwoPages_LogsOneDescriptionAndOneCompletion(
        string call, string path, string? typeParameter)
    {
        GivenTwoPages(path, "owner", typeParameter);

        await InvokeAsync(CreateApi(), call);

        _server.LogEntries.Count.ShouldBe(2);
        var records = _logger.Collector.GetSnapshot();
        records.Count(r => r.Id.Id == _requestingEventId).ShouldBe(1);
        records.Count(r => r.Id.Id == _requestCompletedEventId).ShouldBe(1);
    }

    [Fact]
    public async Task GetRepositoryAsync_Failure_LogsDescriptionWithoutCompletion()
    {
        GivenError("/repos/owner/repo", 502, "Server Error");

        var exception = await Should.ThrowAsync<GitHubApiException>(
            () => CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(502);
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_requestingEventId);
        record.Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task GetRepositoryAsync_Success_FillsLastRateLimit()
    {
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));
        var api = CreateApi();

        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        api.LastRateLimit.ShouldBe(new GitHubRateLimit(5000, 4999, _resetAt));
    }

    [Fact]
    public async Task GetRepositoryAsync_NoRateLimitHeaders_LastRateLimitIsNull()
    {
        _server.Given(Request.Create().WithPath("/repos/owner/repo").UsingGet()).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Repository(1, "owner", "repo")));
        var api = CreateApi();

        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        api.LastRateLimit.ShouldBeNull();
    }

    [Fact]
    public async Task GetRepositoryAsync_RemainingBelowTenPercent_LogsOneWarningPerRun()
    {
        GivenRepositoryWithRemaining("/repos/owner/repo", 499);
        var api = CreateApi();

        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);
        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        var record = _logger.Collector.GetSnapshot().Where(r => r.Id.Id == _rateLimitLowEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Remaining").ShouldBe("499");
        record.GetStructuredStateValue("Limit").ShouldBe("5000");
    }

    [Fact]
    public async Task GetRepositoryAsync_RemainingAtTenPercent_LogsNoWarning()
    {
        GivenRepositoryWithRemaining("/repos/owner/repo", 500);

        await CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot().ShouldNotContain(record => record.Id.Id == _rateLimitLowEventId);
    }

    [Fact]
    public async Task UseToken_AfterLowRateLimitWarning_WarnsAgainInNextRunAndClearsLastRateLimit()
    {
        GivenRepositoryWithRemaining("/repos/owner/repo", 499);
        var api = CreateApi();
        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        api.UseToken(_configuredToken);
        api.LastRateLimit.ShouldBeNull();
        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot().Count(r => r.Id.Id == _rateLimitLowEventId).ShouldBe(2);
    }

    [Fact]
    public async Task GetRepositoryAsync_Authenticated_SendsTokenHeaderAndNeverLogsIt()
    {
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));

        await CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        var headers = _server.LogEntries.ShouldHaveSingleItem().RequestMessage!.Headers!;
        headers.Keys.ShouldContain("Authorization", StringComparer.OrdinalIgnoreCase);
        headers.First(header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            .Value.ToString().ShouldEndWith(_token);
        _logger.Collector.GetSnapshot().ShouldAllBe(record =>
            !record.Message.Contains(_token)
            && (record.StructuredState == null || record.StructuredState.All(pair => pair.Value == null || !pair.Value.Contains(_token))));
    }

    [Fact]
    public async Task GetRepositoryAsync_Anonymous_SendsNoAuthorizationHeader()
    {
        _runToken = GitHubToken.Anonymous;
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));

        await CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        var headers = _server.LogEntries.ShouldHaveSingleItem().RequestMessage!.Headers!;
        headers.Keys.ShouldNotContain("Authorization", StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/v3")]
    [InlineData("/api/v3/")]
    public async Task GetRepositoryAsync_ApiBaseUrlWithPath_UsesPathAsIs(string basePath)
    {
        GivenRepository("/api/v3/repos/owner/repo", Repository(1, "owner", "repo"));

        var repository = await CreateApi(_server.Url + basePath)
            .GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        repository.Name.ShouldBe("repo");
        _server.LogEntries.ShouldHaveSingleItem().RequestMessage!.Path.ShouldBe("/api/v3/repos/owner/repo");
    }

    [Fact]
    public async Task GetRepositoryAsync_LoopbackApiBaseUrl_AddsNoApiPath()
    {
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));

        await CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        _server.LogEntries.ShouldHaveSingleItem().RequestMessage!.Path.ShouldBe("/repos/owner/repo");
    }

    [Theory]
    [MemberData(nameof(InvalidInputCalls))]
    public async Task AnyMethod_InvalidInput_ThrowsArgumentExceptionWithoutRequest(string call)
    {
        var api = CreateApi();
        var cancellationToken = TestContext.Current.CancellationToken;
        Func<Task> action = call switch
        {
            "account" => () => api.GetAccountAsync("-bad-", cancellationToken),
            "organization" => () => api.GetOrganizationRepositoriesAsync("../etc", cancellationToken),
            "user" => () => api.GetUserRepositoriesAsync("a/b", cancellationToken),
            "owner" => () => api.GetRepositoryAsync("", "repo", cancellationToken),
            "name" => () => api.GetRepositoryAsync("owner", "..", cancellationToken),
            _ => () => api.GetRepositoryByIdAsync(0, cancellationToken),
        };

        await Should.ThrowAsync<ArgumentException>(action);

        _server.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetRepositoryAsync_Cancelled_ThrowsOperationCanceledException()
    {
        _server
            .Given(Request.Create().WithPath("/repos/owner/repo").UsingGet())
            .RespondWith(Json(Repository(1, "owner", "repo")).WithDelay(TimeSpan.FromSeconds(10)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var api = CreateApi();

        var call = api.GetRepositoryAsync("owner", "repo", cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task GetRepositoryAsync_NoTokenSet_SendsNoAuthorizationHeader()
    {
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));
        var options = Options.Create(new GitHubOptions { ApiBaseUrl = _server.Url! });
        var api = new OctokitGitHubApi(options, _timeProvider, _logger);

        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        AuthorizationOfRequests().ShouldBe([null]);
    }

    [Fact]
    public async Task UseToken_TokenChanged_NextRequestSendsNewTokenAndNeverLogsEither()
    {
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));
        var api = CreateApi();
        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        api.UseToken(new(_otherToken, "test"));
        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        AuthorizationOfRequests().ShouldBe([_token, _otherToken]);
        _logger.Collector.GetSnapshot().ShouldAllBe(record =>
            !record.Message.Contains(_token)
            && !record.Message.Contains(_otherToken)
            && (record.StructuredState == null || record.StructuredState.All(pair =>
                pair.Value == null || (!pair.Value.Contains(_token) && !pair.Value.Contains(_otherToken)))));
    }

    [Fact]
    public async Task UseToken_TokenThenAnonymousThenToken_SwitchesAuthorizationOnNextRequest()
    {
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));
        var api = CreateApi();
        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        api.UseToken(GitHubToken.Anonymous);
        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);
        api.UseToken(_configuredToken);
        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        AuthorizationOfRequests().ShouldBe([_token, null, _token]);
    }

    [Fact]
    public async Task GetCurrentUserRepositoriesAsync_AnonymousThenToken_RequestsOnlyWithToken()
    {
        _runToken = GitHubToken.Anonymous;
        GivenRepository("/user/repos", RepositoriesJson(Repository(1, "me", "first")));
        var api = CreateApi();
        var skipped = await api.GetCurrentUserRepositoriesAsync(TestContext.Current.CancellationToken);

        api.UseToken(_configuredToken);
        var repositories = await api.GetCurrentUserRepositoriesAsync(TestContext.Current.CancellationToken);

        skipped.ShouldBeEmpty();
        repositories.ShouldHaveSingleItem().Name.ShouldBe("first");
        AuthorizationOfRequests().ShouldBe([_token]);
    }

    [Fact]
    public async Task GetCurrentUserRepositoriesAsync_TokenThenAnonymous_SkipsWithoutRequest()
    {
        GivenRepository("/user/repos", RepositoriesJson(Repository(1, "me", "first")));
        var api = CreateApi();
        var listed = await api.GetCurrentUserRepositoriesAsync(TestContext.Current.CancellationToken);

        api.UseToken(GitHubToken.Anonymous);
        var repositories = await api.GetCurrentUserRepositoriesAsync(TestContext.Current.CancellationToken);

        listed.ShouldHaveSingleItem();
        repositories.ShouldBeEmpty();
        AuthorizationOfRequests().ShouldBe([_token]);
        _logger.Collector.GetSnapshot().ShouldContain(record => record.Id.Id == _currentUserRepositoriesSkippedEventId);
    }

    /// <summary>
    /// Builds the JSON of one repository as GitHub returns it.
    /// </summary>
    /// <param name="id">The repository id.</param>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <param name="isPrivate">The <c>private</c> flag.</param>
    /// <param name="isFork">The <c>fork</c> flag.</param>
    /// <param name="isArchived">The <c>archived</c> flag.</param>
    /// <param name="hasWiki">The <c>has_wiki</c> flag.</param>
    /// <returns>The JSON object.</returns>
    private static string Repository(
        long id,
        string owner,
        string name,
        bool isPrivate = false,
        bool isFork = false,
        bool isArchived = false,
        bool hasWiki = true) =>
        $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "full_name": "{{owner}}/{{name}}",
          "owner": { "login": "{{owner}}", "id": 7, "type": "User" },
          "private": {{Bool(isPrivate)}},
          "fork": {{Bool(isFork)}},
          "archived": {{Bool(isArchived)}},
          "has_wiki": {{Bool(hasWiki)}},
          "default_branch": "develop",
          "html_url": "https://github.com/{{owner}}/{{name}}"
        }
        """;

    /// <summary>
    /// Returns the JSON literal of a flag.
    /// </summary>
    /// <param name="value">The flag.</param>
    /// <returns><c>true</c> or <c>false</c>.</returns>
    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>
    /// Builds the JSON array of a repository list page.
    /// </summary>
    /// <param name="repositories">The repository JSON objects.</param>
    /// <returns>The JSON array.</returns>
    private static string RepositoriesJson(params string[] repositories) => $"[{string.Join(",", repositories)}]";

    /// <summary>
    /// Builds the JSON body of a GitHub error response.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The JSON object.</returns>
    private static string ErrorJson(string message) =>
        $$"""{ "message": "{{message}}", "documentation_url": "https://docs.github.com/rest" }""";

    /// <summary>
    /// Builds a successful JSON response with the rate limit headers GitHub sends.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>The response builder.</returns>
    private static IResponseBuilder Json(string body) => Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "application/json")
        .WithHeader("X-RateLimit-Limit", "5000")
        .WithHeader("X-RateLimit-Remaining", "4999")
        .WithHeader("X-RateLimit-Reset", _resetEpochSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .WithBody(body);

    /// <summary>
    /// Creates the subject against the server, or against <paramref name="apiBaseUrl"/> when given.
    /// </summary>
    /// <param name="apiBaseUrl">The API address, or <see langword="null"/> for the server root.</param>
    /// <returns>The subject, with <see cref="_runToken"/> set.</returns>
    private OctokitGitHubApi CreateApi(string? apiBaseUrl = null)
    {
        var options = Options.Create(new GitHubOptions { ApiBaseUrl = apiBaseUrl ?? _server.Url! });
        var api = new OctokitGitHubApi(options, _timeProvider, _logger);
        api.UseToken(_runToken);
        return api;
    }

    /// <summary>
    /// Runs the API call named by <paramref name="call"/>, one of the names in <see cref="DescribedCalls"/>.
    /// </summary>
    /// <param name="api">The subject.</param>
    /// <param name="call">The name of the call.</param>
    /// <returns>A task that completes with the call.</returns>
    private static Task InvokeAsync(OctokitGitHubApi api, string call)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        return call switch
        {
            "account" => api.GetAccountAsync("octocat", cancellationToken),
            "current" => api.GetCurrentUserRepositoriesAsync(cancellationToken),
            "organization" => api.GetOrganizationRepositoriesAsync("acme", cancellationToken),
            "user" => api.GetUserRepositoriesAsync("octocat", cancellationToken),
            "repository" => api.GetRepositoryAsync("octocat", "repo", cancellationToken),
            _ => api.GetRepositoryByIdAsync(42, cancellationToken),
        };
    }

    /// <summary>
    /// Serves a successful response for <paramref name="path"/>: a list page, an account or a repository.
    /// </summary>
    /// <param name="path">A path of <see cref="DescribedCalls"/>.</param>
    private void GivenCallResponse(string path)
    {
        var body = path.EndsWith("/repos", StringComparison.Ordinal)
            ? RepositoriesJson(Repository(1, "octocat", "repo"))
            : path.StartsWith("/users/", StringComparison.Ordinal)
                ? """{ "login": "octocat", "id": 7, "type": "User" }"""
                : Repository(42, "octocat", "repo");
        _server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(Json(body));
    }

    /// <summary>
    /// Returns the credential of the <c>Authorization</c> header of every request the server received, in order.
    /// </summary>
    /// <returns>
    /// The header values without the scheme; <see langword="null"/> for a request without the header.
    /// </returns>
    private string?[] AuthorizationOfRequests() =>
    [
        .. _server.LogEntries.Select(entry => entry.RequestMessage!.Headers!
            .Where(header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value.ToString()!.Split(' ')[^1])
            .FirstOrDefault()),
    ];

    /// <summary>
    /// Serves the first page of a repository list with a <c>Link</c> header whose next page is <paramref name="nextUrl"/>.
    /// </summary>
    /// <param name="path">The list path.</param>
    /// <param name="nextUrl">The absolute next-page link.</param>
    private void GivenFirstPageLinkingTo(string path, string nextUrl) =>
        _server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Json(RepositoriesJson(Repository(1, "octocat", "first")))
                .WithHeader("Link", $"<{nextUrl}>; rel=\"next\""));

    /// <summary>
    /// Serves a repository list in two pages linked by a <c>Link</c> header; both pages require
    /// <c>per_page=100</c> and, when given, <c>type=all</c>.
    /// </summary>
    /// <param name="path">The list path.</param>
    /// <param name="owner">The owner of the listed repositories.</param>
    /// <param name="typeParameter">The name of the parameter that must be <c>all</c>, or <see langword="null"/>.</param>
    private void GivenTwoPages(string path, string owner, string? typeParameter = null)
    {
        var firstPage = Request.Create().WithPath(path).WithParam("per_page", "100").UsingGet();
        var secondPage = Request.Create().WithPath(path).WithParam("page", "2").UsingGet();
        if (typeParameter is not null)
        {
            firstPage = firstPage.WithParam(typeParameter, "all");
            secondPage = secondPage.WithParam(typeParameter, "all");
        }

        var typeQuery = typeParameter is null ? string.Empty : $"{typeParameter}=all&";
        var nextUrl = $"{_server.Url}{path}?{typeQuery}per_page=100&page=2";
        _server
            .Given(secondPage)
            .AtPriority(1)
            .RespondWith(Json(RepositoriesJson(Repository(3, owner, "third"))));
        _server
            .Given(firstPage)
            .AtPriority(2)
            .RespondWith(Json(RepositoriesJson(Repository(1, owner, "first"), Repository(2, owner, "second")))
                .WithHeader("Link", $"<{nextUrl}>; rel=\"next\", <{nextUrl}>; rel=\"last\""));
    }

    /// <summary>
    /// Serves one repository.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="repository">The repository JSON.</param>
    private void GivenRepository(string path, string repository) =>
        _server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(Json(repository));

    /// <summary>
    /// Serves a repository whose response reports <paramref name="remaining"/> of 5000 requests left.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="remaining">The remaining request count.</param>
    private void GivenRepositoryWithRemaining(string path, int remaining) =>
        _server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(
            Json(Repository(1, "owner", "repo"))
                .WithHeader("X-RateLimit-Remaining", remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    /// Serves one account.
    /// </summary>
    /// <param name="login">The login in the request path.</param>
    /// <param name="serverLogin">The login in the letter case the server uses.</param>
    /// <param name="type">The <c>type</c> field of the account.</param>
    private void GivenAccount(string login, string serverLogin, string type) =>
        _server
            .Given(Request.Create().WithPath($"/users/{login}").UsingGet())
            .RespondWith(Json($$"""{ "login": "{{serverLogin}}", "id": 7, "type": "{{type}}" }"""));

    /// <summary>
    /// Serves an error response.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="message">The GitHub error message.</param>
    private void GivenError(string path, int statusCode, string message) =>
        _server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(ErrorJson(message)));

    /// <summary>
    /// Returns the decoded query parameters of the only request the server received, sorted.
    /// </summary>
    /// <returns>The <c>name=value</c> pairs.</returns>
    private string[] QueryOfOnlyRequest()
    {
        var query = new Uri(_server.LogEntries.ShouldHaveSingleItem().RequestMessage!.Url).Query.TrimStart('?');
        return [.. query.Split('&').Select(Uri.UnescapeDataString).Order(StringComparer.Ordinal)];
    }
}
