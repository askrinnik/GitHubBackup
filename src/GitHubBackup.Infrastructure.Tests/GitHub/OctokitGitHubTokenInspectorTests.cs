using System.Globalization;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.GitHub;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace GitHubBackup.Infrastructure.Tests.GitHub;

/// <summary>
/// Contract tests of <see cref="OctokitGitHubTokenInspector"/> against a WireMock.Net server on the loopback interface.
/// </summary>
public sealed class OctokitGitHubTokenInspectorTests : IDisposable
{
    private const string _token = "ghp_InspectorTokenValue0123456789abcdefAB";
    private const long _resetEpochSeconds = 1_893_456_000;

    private static readonly DateTimeOffset _resetAt = DateTimeOffset.FromUnixTimeSeconds(_resetEpochSeconds);

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly SecretMasker _masker = new();

    /// <inheritdoc />
    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task InspectAsync_ClassicToken_ReturnsLoginScopesAndRateLimit()
    {
        _server
            .Given(Request.Create().WithPath("/user").UsingGet())
            .RespondWith(Json(UserJson("octocat")).WithHeader("X-OAuth-Scopes", "repo, read:org"));

        var info = await CreateInspector().InspectAsync(_token, TestContext.Current.CancellationToken);

        info.ShouldBe(new GitHubTokenInfo("octocat", info.Scopes, new(5000, 4999, _resetAt)));
        info.Scopes.ShouldBe(["repo", "read:org"]);
        info.IsAnonymous.ShouldBeFalse();
    }

    [Fact]
    public async Task InspectAsync_NoScopesHeader_ReturnsNullScopes()
    {
        _server.Given(Request.Create().WithPath("/user").UsingGet()).RespondWith(Json(UserJson("octocat")));

        var info = await CreateInspector().InspectAsync(_token, TestContext.Current.CancellationToken);

        info.Login.ShouldBe("octocat");
        info.Scopes.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InspectAsync_NoToken_RequestsRateLimitWithoutAuthorization(string? token)
    {
        _server
            .Given(Request.Create().WithPath("/rate_limit").UsingGet())
            .RespondWith(Json($$"""
                {
                  "resources": { "core": { "limit": 60, "remaining": 57, "reset": {{_resetEpochSeconds}}, "used": 3 } },
                  "rate": { "limit": 60, "remaining": 57, "reset": {{_resetEpochSeconds}}, "used": 3 }
                }
                """));

        var info = await CreateInspector().InspectAsync(token, TestContext.Current.CancellationToken);

        info.ShouldBe(new GitHubTokenInfo(null, null, new(60, 57, _resetAt)));
        info.IsAnonymous.ShouldBeTrue();
        var headers = _server.LogEntries.ShouldHaveSingleItem().RequestMessage!.Headers!;
        headers.Keys.ShouldNotContain("Authorization", StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectAsync_Token_SendsItOnlyToConfiguredApiPathAndRegistersItWithMasker()
    {
        _server.Given(Request.Create().WithPath("/api/v3/user").UsingGet()).RespondWith(Json(UserJson("octocat")));

        await CreateInspector(_server.Url + "/api/v3").InspectAsync(_token, TestContext.Current.CancellationToken);

        var request = _server.LogEntries.ShouldHaveSingleItem().RequestMessage!;
        request.Path.ShouldBe("/api/v3/user");
        request.Headers!.First(header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            .Value.ToString().ShouldEndWith(_token);
        _masker.Mask($"value {_token}").ShouldBe("value ***");
    }

    [Fact]
    public async Task InspectAsync_Rejected_ThrowsAuthenticationExceptionWithoutToken()
    {
        GivenError(401, "Bad credentials");

        var exception = await Should.ThrowAsync<GitHubAuthenticationException>(
            () => CreateInspector().InspectAsync(_token, TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(401);
        exception.Message.ShouldNotContain(_token);
    }

    [Fact]
    public async Task InspectAsync_ServerError_ThrowsGitHubApiException()
    {
        GivenError(502, "Server Error");

        var exception = await Should.ThrowAsync<GitHubApiException>(
            () => CreateInspector().InspectAsync(_token, TestContext.Current.CancellationToken));

        exception.ShouldNotBeOfType<GitHubAuthenticationException>();
        exception.StatusCode.ShouldBe(502);
        exception.Message.ShouldNotContain(_token);
    }

    [Fact]
    public async Task InspectAsync_Cancelled_ThrowsOperationCanceledException()
    {
        _server
            .Given(Request.Create().WithPath("/user").UsingGet())
            .RespondWith(Json(UserJson("octocat")).WithDelay(TimeSpan.FromSeconds(10)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var check = CreateInspector().InspectAsync(_token, cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => check);
    }

    /// <summary>
    /// Builds the JSON body of <c>GET /user</c>.
    /// </summary>
    /// <param name="login">The login of the account.</param>
    /// <returns>The JSON object.</returns>
    private static string UserJson(string login) => $$"""{ "login": "{{login}}", "id": 7, "type": "User" }""";

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
        .WithHeader("X-RateLimit-Reset", _resetEpochSeconds.ToString(CultureInfo.InvariantCulture))
        .WithBody(body);

    /// <summary>
    /// Serves an error response for <c>GET /user</c>.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="message">The GitHub error message.</param>
    private void GivenError(int statusCode, string message) =>
        _server
            .Given(Request.Create().WithPath("/user").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody($$"""{ "message": "{{message}}" }"""));

    /// <summary>
    /// Creates the subject against the server, or against <paramref name="apiBaseUrl"/> when given.
    /// </summary>
    /// <param name="apiBaseUrl">The API address, or <see langword="null"/> for the server root.</param>
    /// <returns>The subject.</returns>
    private OctokitGitHubTokenInspector CreateInspector(string? apiBaseUrl = null) => new(
        Options.Create(new GitHubOptions { ApiBaseUrl = apiBaseUrl ?? _server.Url! }),
        _masker,
        new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero)));
}
