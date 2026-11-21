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
/// The token set by <see cref="UseToken"/> goes with every following request. After every successful call the rate
/// limit state of the last response is kept in <see cref="LastRateLimit"/> and logged at debug level. A warning is
/// logged once per run when less than a tenth of the limit remains; <see cref="UseToken"/>, which starts a run, resets
/// the warning and <see cref="LastRateLimit"/>.
/// </remarks>
/// <param name="options">The GitHub options that hold the API address.</param>
/// <param name="timeProvider">
/// The clock from which a <c>Retry-After</c> delay is counted and that measures the duration of a request.
/// </param>
/// <param name="logger">The logger.</param>
internal sealed class OctokitGitHubApi(
    IOptions<GitHubOptions> options,
    TimeProvider timeProvider,
    ILogger<OctokitGitHubApi> logger) : IGitHubApi
{
    private const string _pageSize = "100";

    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<OctokitGitHubApi> _logger = logger;
    private readonly GitHubClient _client = new(OctokitConnectionFactory.Create(options.Value.ApiBaseUrl));
    private volatile bool _isAnonymous = true;
    private volatile GitHubRateLimit? _lastRateLimit;
    private int _lowRateLimitWarned;

    /// <inheritdoc />
    public GitHubRateLimit? LastRateLimit => _lastRateLimit;

    /// <inheritdoc />
    public void UseToken(GitHubToken token)
    {
        _lastRateLimit = null;
        Volatile.Write(ref _lowRateLimitWarned, 0);
        _client.Credentials = token.Value is { } value ? new Credentials(value) : Credentials.Anonymous;
        _isAnonymous = token.IsAnonymous;
    }

    /// <inheritdoc />
    public Task<GitHubAccount> GetAccountAsync(string login, CancellationToken cancellationToken)
    {
        EnsureValidLogin(login, nameof(login));

        return ExecuteAsync(
            $"users/{login}",
            "Requesting the account of an owner",
            async () => OctokitModelMapper.ToAccount(await _client.User.Get(login)),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubRepository>> GetCurrentUserRepositoriesAsync(CancellationToken cancellationToken)
    {
        if (_isAnonymous)
        {
            OctokitGitHubApiLog.CurrentUserRepositoriesSkipped(_logger);
            return [];
        }

        return await GetAllRepositoriesAsync(
            "user/repos",
            "Requesting the repositories of the authenticated user",
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
            "Requesting the repositories of an organization",
            new() { ["type"] = "all" },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(string login, CancellationToken cancellationToken)
    {
        EnsureValidLogin(login, nameof(login));

        return GetAllRepositoriesAsync(
            $"users/{Uri.EscapeDataString(login)}/repos",
            "Requesting the repositories of a user",
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
            "Requesting a repository by owner and name",
            async () => OctokitModelMapper.ToRepository(await _client.Repository.Get(owner, name)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GitHubRepository> GetRepositoryByIdAsync(long id, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        return ExecuteAsync(
            $"repositories/{id}",
            "Requesting a repository by its id",
            async () => OctokitModelMapper.ToRepository(await _client.Repository.Get(id)),
            cancellationToken);
    }

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
    /// <param name="description">The description logged once before the first page is requested.</param>
    /// <param name="parameters">The query parameters other than the page size.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The repositories of all pages.</returns>
    private Task<IReadOnlyList<GitHubRepository>> GetAllRepositoriesAsync(
        string resource, string description, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        parameters["per_page"] = _pageSize;

        return ExecuteAsync<IReadOnlyList<GitHubRepository>>(
            resource,
            description,
            async () =>
            {
                List<GitHubRepository> repositories = [];
                Uri? page = new(resource, UriKind.Relative);
                IDictionary<string, string>? query = parameters;
                while (page is not null)
                {
                    var response = await _client.Connection.Get<List<Repository>>(page, query, null, cancellationToken);
                    repositories.AddRange(response.Body.Select(OctokitModelMapper.ToRepository));

                    // The next-page link already carries the query.
                    page = EnsureNextPageOnApi(response.HttpResponse.ApiInfo.GetNextPageUrl(), resource);
                    query = null;
                }

                return repositories;
            },
            cancellationToken);
    }

    /// <summary>
    /// Accepts a next-page link only when it points under the configured API address, because the credentials go
    /// with every page request and a server must not be able to send them to another host.
    /// </summary>
    /// <param name="next">The next-page link of the last response, or <see langword="null"/> on the last page.</param>
    /// <param name="resource">The API resource, named in the failure message.</param>
    /// <returns><paramref name="next"/>, or <see langword="null"/> when there is no further page.</returns>
    /// <exception cref="GitHubApiException">The link points outside the configured API address.</exception>
    private Uri? EnsureNextPageOnApi(Uri? next, string resource)
    {
        if (next is null)
        {
            return null;
        }

        var baseAddress = _client.Connection.BaseAddress;
        if (next.IsAbsoluteUri
            && Uri.Compare(next, baseAddress, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0
            && next.AbsolutePath.StartsWith(baseAddress.AbsolutePath, StringComparison.Ordinal))
        {
            return next;
        }

        throw new GitHubApiException(
            $"The GitHub API returned a next-page link for {resource} outside the configured API address.",
            resource,
            null);
    }

    /// <summary>
    /// Runs one API operation with the credentials applied, converts its failures into the
    /// <see cref="GitHubApiException"/> family and logs the rate limit state afterwards.
    /// </summary>
    /// <remarks>
    /// The description goes to the log before the operation at information level; the resource and the duration
    /// follow at debug level only after a success, because a failure already names the resource in its exception.
    /// </remarks>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="resource">The API resource, named in failure messages and in the completion entry.</param>
    /// <param name="description">The constant description of the operation, without logins or repository names.</param>
    /// <param name="operation">The Octokit call.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the call.</param>
    /// <returns>The result of the operation.</returns>
    private async Task<T> ExecuteAsync<T>(
        string resource, string description, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        OctokitGitHubApiLog.Requesting(_logger, description);
        var startedAt = _timeProvider.GetTimestamp();
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

        var duration = _timeProvider.GetElapsedTime(startedAt);
        OctokitGitHubApiLog.RequestCompleted(_logger, resource, duration);
        LogRateLimit();
        return result;
    }

    /// <summary>
    /// Records the rate limit state of the last response, when the server reported one, and logs it. The low-limit
    /// warning is logged by the first caller that sees the state; concurrent callers do not repeat it.
    /// </summary>
    private void LogRateLimit()
    {
        var rateLimit = _client.GetLastApiInfo()?.RateLimit;
        if (rateLimit is { Limit: > 0 })
        {
            _lastRateLimit = new(rateLimit.Limit, rateLimit.Remaining, rateLimit.Reset);
            OctokitGitHubApiLog.RateLimit(_logger, rateLimit.Remaining, rateLimit.Limit, rateLimit.Reset);
            if (rateLimit.Remaining * 10 < rateLimit.Limit && Interlocked.Exchange(ref _lowRateLimitWarned, 1) == 0)
            {
                OctokitGitHubApiLog.RateLimitLow(_logger, rateLimit.Remaining, rateLimit.Limit, rateLimit.Reset);
            }
        }
    }
}
