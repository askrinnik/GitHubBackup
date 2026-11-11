using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace GitHubBackup.Infrastructure.GitHub;

/// <summary>
/// Implements <see cref="IGitHubApi"/> with Octokit.NET against the address in <see cref="GitHubOptions.ApiBaseUrl"/>.
/// </summary>
/// <remarks>
/// The token is resolved through <see cref="ITokenProvider"/> on the first call and kept for the lifetime of the
/// instance. After every successful call the rate limit state of the last response is logged.
/// </remarks>
internal sealed class OctokitGitHubApi : IGitHubApi, IDisposable
{
    private const string _productName = "GitHubBackup";
    private const string _pageSize = "100";

    private readonly ITokenProvider _tokenProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OctokitGitHubApi> _logger;
    private readonly GitHubClient _client;
    private readonly ApiConnection _apiConnection;
    private readonly SemaphoreSlim _credentialsLock = new(1, 1);
    private volatile bool _credentialsResolved;
    private bool _isAnonymous;

    /// <summary>
    /// Initializes the client for the configured API address.
    /// </summary>
    /// <param name="tokenProvider">The provider of the token, asked once on the first call.</param>
    /// <param name="options">The GitHub options that hold the API address.</param>
    /// <param name="timeProvider">The clock from which a <c>Retry-After</c> delay is counted.</param>
    /// <param name="logger">The logger.</param>
    public OctokitGitHubApi(
        ITokenProvider tokenProvider,
        IOptions<GitHubOptions> options,
        TimeProvider timeProvider,
        ILogger<OctokitGitHubApi> logger)
    {
        _tokenProvider = tokenProvider;
        _timeProvider = timeProvider;
        _logger = logger;

        // A Connection keeps the base address as given; the GitHubClient constructor that takes an address would
        // append api/v3 to every host other than api.github.com.
        var connection = new Connection(
            new ProductHeaderValue(_productName, ProductVersion()),
            CreateBaseAddress(options.Value.ApiBaseUrl));
        _client = new(connection);
        _apiConnection = new(connection);
    }

    /// <inheritdoc />
    public Task<GitHubAccount> GetAccountAsync(string login, CancellationToken cancellationToken)
    {
        EnsureValidLogin(login, nameof(login));

        return ExecuteAsync(
            $"users/{login}",
            async () => OctokitModelMapper.ToAccount(await _client.User.Get(login)),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubRepository>> GetCurrentUserRepositoriesAsync(CancellationToken cancellationToken)
    {
        if (await IsAnonymousAsync(cancellationToken))
        {
            OctokitGitHubApiLog.CurrentUserRepositoriesSkipped(_logger);
            return [];
        }

        return await GetAllRepositoriesAsync(
            "user/repos",
            new()
            {
                ["affiliation"] = "owner,collaborator,organization_member",
                ["visibility"] = "all",
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHubRepository>> GetOrganizationRepositoriesAsync(
        string organization, CancellationToken cancellationToken)
    {
        EnsureValidLogin(organization, nameof(organization));

        return GetAllRepositoriesAsync(
            $"orgs/{Uri.EscapeDataString(organization)}/repos",
            new() { ["type"] = "all" },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(string login, CancellationToken cancellationToken)
    {
        EnsureValidLogin(login, nameof(login));

        return GetAllRepositoriesAsync(
            $"users/{Uri.EscapeDataString(login)}/repos",
            new() { ["type"] = "all" },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken cancellationToken)
    {
        EnsureValidLogin(owner, nameof(owner));
        if (!GitHubUrl.IsValidRepositoryName(name))
        {
            throw new ArgumentException($"'{name}' is not a valid GitHub repository name.", nameof(name));
        }

        return ExecuteAsync(
            $"repos/{owner}/{name}",
            async () => OctokitModelMapper.ToRepository(await _client.Repository.Get(owner, name)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GitHubRepository> GetRepositoryByIdAsync(long id, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        return ExecuteAsync(
            $"repositories/{id}",
            async () => OctokitModelMapper.ToRepository(await _client.Repository.Get(id)),
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _credentialsLock.Dispose();

    /// <summary>
    /// Returns the version sent in the <c>User-Agent</c> header.
    /// </summary>
    /// <returns>The version of this assembly.</returns>
    private static string ProductVersion() =>
        typeof(OctokitGitHubApi).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Returns the API address with a trailing slash, without which a relative resource would replace the last
    /// path segment of the address instead of being appended to it.
    /// </summary>
    /// <param name="apiBaseUrl">The configured API address.</param>
    /// <returns>The base address.</returns>
    private static Uri CreateBaseAddress(string apiBaseUrl) =>
        new(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/", UriKind.Absolute);

    /// <summary>
    /// Rejects a value that is not a GitHub login, so it never becomes part of a request path.
    /// </summary>
    /// <param name="login">The value to check.</param>
    /// <param name="parameterName">The name of the parameter that holds the value.</param>
    /// <exception cref="ArgumentException"><paramref name="login"/> is not a valid GitHub login.</exception>
    private static void EnsureValidLogin(string login, string parameterName)
    {
        if (!GitHubUrl.IsValidLogin(login))
        {
            throw new ArgumentException($"'{login}' is not a valid GitHub login.", parameterName);
        }
    }

    /// <summary>
    /// Requests every page of a repository list.
    /// </summary>
    /// <param name="resource">The relative resource, with its path segments escaped.</param>
    /// <param name="parameters">The query parameters other than the page size.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The repositories of all pages.</returns>
    private Task<IReadOnlyList<GitHubRepository>> GetAllRepositoriesAsync(
        string resource, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        parameters["per_page"] = _pageSize;

        return ExecuteAsync<IReadOnlyList<GitHubRepository>>(
            resource,
            async () =>
            {
                var repositories = await _apiConnection.GetAll<Repository>(new(resource, UriKind.Relative), parameters);
                return [.. repositories.Select(OctokitModelMapper.ToRepository)];
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs one API operation with the credentials applied, converts its failures into the
    /// <see cref="GitHubApiException"/> family and logs the rate limit state afterwards.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="resource">The API resource, named in failure messages.</param>
    /// <param name="operation">The Octokit call.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the call.</param>
    /// <returns>The result of the operation.</returns>
    private async Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await IsAnonymousAsync(cancellationToken);

        T result;
        try
        {
            // Octokit's clients take no cancellation token, so cancellation stops the wait, not the request.
            result = await operation().WaitAsync(cancellationToken);
        }
        catch (ApiException exception)
        {
            throw OctokitExceptionMapper.Map(resource, exception, _timeProvider.GetUtcNow());
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubApiException(
                $"The GitHub API request for {resource} failed: {exception.Message}",
                resource,
                exception.StatusCode is { } status ? (int)status : null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubApiException($"The GitHub API request for {resource} timed out.", resource, null);
        }

        LogRateLimit();
        return result;
    }

    /// <summary>
    /// Resolves the token on the first call, applies it to the client and tells whether access is anonymous.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the lookup.</param>
    /// <returns><see langword="true"/> when no token is configured.</returns>
    private async Task<bool> IsAnonymousAsync(CancellationToken cancellationToken)
    {
        if (_credentialsResolved)
        {
            return _isAnonymous;
        }

        await _credentialsLock.WaitAsync(cancellationToken);
        try
        {
            if (!_credentialsResolved)
            {
                var token = await _tokenProvider.GetTokenAsync(cancellationToken);
                _client.Credentials = token.Value is { } value ? new Credentials(value) : Credentials.Anonymous;
                _isAnonymous = token.IsAnonymous;
                _credentialsResolved = true;
            }
        }
        finally
        {
            _credentialsLock.Release();
        }

        return _isAnonymous;
    }

    /// <summary>
    /// Logs the rate limit state of the last response, when the server reported one.
    /// </summary>
    private void LogRateLimit()
    {
        var rateLimit = _client.GetLastApiInfo()?.RateLimit;
        if (rateLimit is { Limit: > 0 })
        {
            OctokitGitHubApiLog.RateLimit(_logger, rateLimit.Remaining, rateLimit.Limit, rateLimit.Reset);
        }
    }
}
