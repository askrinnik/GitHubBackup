using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.GitHub;
using GitHubBackup.Infrastructure.Tests.BackupConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
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

    private static readonly DateTimeOffset _now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _resetAt = DateTimeOffset.FromUnixTimeSeconds(_resetEpochSeconds);

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();
    private readonly RecordingLogger<OctokitGitHubApi> _logger = new();
    private readonly FakeTimeProvider _timeProvider = new(_now);
    private readonly List<OctokitGitHubApi> _apis = [];

    /// <summary>
    /// Starts the server and sets up a token provider that returns a configured token.
    /// </summary>
    public OctokitGitHubApiTests() =>
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new GitHubToken(_token, "test"));

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

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var api in _apis)
        {
            api.Dispose();
        }

        _server.Dispose();
    }

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
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(GitHubToken.Anonymous);

        var repositories = await CreateApi().GetCurrentUserRepositoriesAsync(TestContext.Current.CancellationToken);

        repositories.ShouldBeEmpty();
        _server.LogEntries.ShouldBeEmpty();
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug);
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
    public async Task GetRepositoryAsync_Success_LogsRemainingRateLimit()
    {
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));

        await CreateApi().GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldStartWith("GitHub API rate limit: 4999/5000 remaining, resets at ");
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
        _logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(_token));
    }

    [Fact]
    public async Task GetRepositoryAsync_Anonymous_SendsNoAuthorizationHeader()
    {
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(GitHubToken.Anonymous);
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
        await _tokenProvider.DidNotReceive().GetTokenAsync(Arg.Any<CancellationToken>());
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
    public async Task GetRepositoryAsync_TwoCalls_ResolvesTokenOnce()
    {
        GivenRepository("/repos/owner/repo", Repository(1, "owner", "repo"));
        var api = CreateApi();

        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);
        await api.GetRepositoryAsync("owner", "repo", TestContext.Current.CancellationToken);

        await _tokenProvider.Received(1).GetTokenAsync(Arg.Any<CancellationToken>());
        _server.LogEntries.Count.ShouldBe(2);
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
    /// <returns>The subject; the test disposes it.</returns>
    private OctokitGitHubApi CreateApi(string? apiBaseUrl = null)
    {
        var options = Options.Create(new GitHubOptions { ApiBaseUrl = apiBaseUrl ?? _server.Url! });
        var api = new OctokitGitHubApi(_tokenProvider, options, _timeProvider, _logger);
        _apis.Add(api);
        return api;
    }

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
