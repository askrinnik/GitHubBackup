using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Octokit;
using Octokit.Internal;

namespace GitHubBackup.Infrastructure.GitHub;

/// <summary>
/// Implements <see cref="IGitHubTokenInspector"/> with Octokit.NET against the address in
/// <see cref="GitHubOptions.ApiBaseUrl"/>.
/// </summary>
/// <remarks>
/// Every check uses a connection of its own, so the token being checked never reaches the client that the backup
/// uses. With a token the check requests <c>GET /user</c>; without one it requests <c>GET /rate_limit</c>, which
/// does not count against the limit.
/// </remarks>
/// <param name="options">The GitHub options that hold the API address.</param>
/// <param name="secretMasker">The masker that learns the token before it is sent.</param>
/// <param name="timeProvider">The clock from which a <c>Retry-After</c> delay is counted.</param>
internal sealed class OctokitGitHubTokenInspector(
    IOptions<GitHubOptions> options,
    ISecretMasker secretMasker,
    TimeProvider timeProvider) : IGitHubTokenInspector
{
    /// <summary>The resource that describes the authenticated account.</summary>
    private const string _userResource = "user";

    /// <summary>The resource that describes the rate limit.</summary>
    private const string _rateLimitResource = "rate_limit";

    /// <summary>The response header that lists the OAuth scopes of a classic token.</summary>
    private const string _scopesHeader = "X-OAuth-Scopes";

    /// <inheritdoc />
    public Task<GitHubTokenInfo> InspectAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ExecuteAsync(_rateLimitResource, Credentials.Anonymous, InspectAnonymousAsync, cancellationToken);
        }

        var value = token.Trim();
        secretMasker.Register(value);
        return ExecuteAsync(_userResource, new(value), InspectTokenAsync, cancellationToken);
    }

    /// <summary>
    /// Requests the authenticated account and reads the scopes and the rate limit from the response headers.
    /// </summary>
    /// <param name="connection">The connection with the token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The information about the token.</returns>
    private static async Task<GitHubTokenInfo> InspectTokenAsync(IConnection connection, CancellationToken cancellationToken)
    {
        var response = await connection.Get<User>(new(_userResource, UriKind.Relative), null, null, cancellationToken);
        var headers = response.HttpResponse.Headers;
        var scopesValue = headers
            .FirstOrDefault(header => header.Key.Equals(_scopesHeader, StringComparison.OrdinalIgnoreCase))
            .Value;

        // A fine-grained token gets no scopes header at all, while a classic token without scopes gets an empty one.
        IReadOnlyList<string>? scopes = scopesValue is null
            ? null
            : [.. scopesValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        var rateLimit = response.HttpResponse.ApiInfo?.RateLimit is { Limit: > 0 } limit
            ? new GitHubRateLimit(limit.Limit, limit.Remaining, limit.Reset)
            : null;

        return new(response.Body.Login, scopes, rateLimit);
    }

    /// <summary>
    /// Requests the rate limit of anonymous access.
    /// </summary>
    /// <param name="connection">The connection without credentials.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The information about anonymous access.</returns>
    private static async Task<GitHubTokenInfo> InspectAnonymousAsync(IConnection connection, CancellationToken cancellationToken)
    {
        var response = await connection.Get<MiscellaneousRateLimit>(
            new(_rateLimitResource, UriKind.Relative), null, null, cancellationToken);
        var core = response.Body?.Resources?.Core;
        var rateLimit = core is { Limit: > 0 }
            ? new GitHubRateLimit(core.Limit, core.Remaining, core.Reset)
            : null;

        return new(null, null, rateLimit);
    }

    /// <summary>
    /// Runs one check on a new connection and converts its failures into the <see cref="GitHubApiException"/> family.
    /// </summary>
    /// <param name="resource">The API resource, named in failure messages.</param>
    /// <param name="credentials">The credentials of the connection.</param>
    /// <param name="operation">The request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The information about the token.</returns>
    private async Task<GitHubTokenInfo> ExecuteAsync(
        string resource,
        Credentials credentials,
        Func<IConnection, CancellationToken, Task<GitHubTokenInfo>> operation,
        CancellationToken cancellationToken)
    {
        // Each check gets its own connection so that a typed, unsaved token never reaches the shared API client;
        // the HTTP client is disposed with the check so that repeated checks do not leak handlers and sockets.
        using var httpClient = new HttpClientAdapter(HttpMessageHandlerFactory.CreateDefault);
        var connection = OctokitConnectionFactory.Create(options.Value.ApiBaseUrl, credentials, httpClient);
        try
        {
            return await operation(connection, cancellationToken).WaitAsync(cancellationToken);
        }
        catch (ApiException exception)
        {
            throw OctokitExceptionMapper.Map(resource, exception, timeProvider.GetUtcNow());
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubApiException(
                $"The GitHub API request for {resource} failed: {secretMasker.Mask(exception.Message)}",
                resource,
                exception.StatusCode is { } status ? (int)status : null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubApiException($"The GitHub API request for {resource} timed out.", resource, null);
        }
    }
}
