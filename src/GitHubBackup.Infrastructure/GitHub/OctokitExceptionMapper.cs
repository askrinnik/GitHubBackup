using System.Globalization;
using System.Net;
using GitHubBackup.Core.GitHub;
using Octokit;

namespace GitHubBackup.Infrastructure.GitHub;

/// <summary>
/// Converts Octokit exceptions into the <see cref="GitHubApiException"/> family.
/// </summary>
/// <remarks>
/// The Octokit exception is not kept as the inner exception: it holds the HTTP response, and a log sink that
/// serializes exceptions must not get a chance to write request details. The message carries the resource, the
/// status code and the error text GitHub returned.
/// </remarks>
internal static class OctokitExceptionMapper
{
    private const string _retryAfterHeader = "Retry-After";

    /// <summary>
    /// Converts an Octokit API exception.
    /// </summary>
    /// <param name="resource">The API resource the request was for.</param>
    /// <param name="exception">The exception Octokit threw.</param>
    /// <param name="now">The current time, from which a <c>Retry-After</c> delay is counted.</param>
    /// <returns>The domain exception to throw.</returns>
    public static GitHubApiException Map(string resource, ApiException exception, DateTimeOffset now)
    {
        var statusCode = (int)exception.StatusCode;

        return exception switch
        {
            RateLimitExceededException rateLimit => RateLimitExceeded(
                resource, statusCode, rateLimit.Reset, rateLimit.Limit, rateLimit.Remaining),
            SecondaryRateLimitExceededException or { StatusCode: HttpStatusCode.TooManyRequests } => RateLimitExceeded(
                resource,
                statusCode,
                ReadRetryAfter(exception.HttpResponse, now),
                exception.HttpResponse?.ApiInfo?.RateLimit?.Limit ?? 0,
                exception.HttpResponse?.ApiInfo?.RateLimit?.Remaining ?? 0),
            AuthorizationException => new GitHubAuthenticationException(
                Describe("GitHub rejected the credentials", resource, statusCode, exception), resource),
            ForbiddenException => new GitHubAccessDeniedException(
                Describe("GitHub denied access", resource, statusCode, exception), resource),
            NotFoundException => new GitHubNotFoundException(
                Describe("GitHub did not find the resource", resource, statusCode, exception), resource),
            _ => new GitHubApiException(
                Describe("The GitHub API request failed", resource, statusCode, exception), resource, statusCode),
        };
    }

    /// <summary>
    /// Creates the exception for an exhausted rate limit.
    /// </summary>
    /// <param name="resource">The API resource the request was for.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="resetAt">The time the limit resets, or <see langword="null"/> when unknown.</param>
    /// <param name="limit">The number of requests allowed in the current window.</param>
    /// <param name="remaining">The number of requests left in the current window.</param>
    /// <returns>The exception.</returns>
    private static GitHubRateLimitExceededException RateLimitExceeded(
        string resource, int statusCode, DateTimeOffset? resetAt, int limit, int remaining)
    {
        var reset = resetAt is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"the limit resets at {value:O}")
            : "the server did not report when the limit resets";
        var message = $"The GitHub API rate limit is exceeded (HTTP {statusCode}) for {resource}; {reset}.";

        return new(message, resource, statusCode, resetAt, limit, remaining);
    }

    /// <summary>
    /// Builds the message of a failed request.
    /// </summary>
    /// <param name="summary">The sentence that names the outcome.</param>
    /// <param name="resource">The API resource the request was for.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="exception">The exception Octokit threw, whose GitHub error text is appended.</param>
    /// <returns>The message.</returns>
    private static string Describe(string summary, string resource, int statusCode, ApiException exception)
    {
        var message = $"{summary} (HTTP {statusCode}) for {resource}";
        var serverMessage = exception.ApiError?.Message;

        return string.IsNullOrWhiteSpace(serverMessage) ? message + "." : $"{message}: {serverMessage}";
    }

    /// <summary>
    /// Reads the <c>Retry-After</c> header, which holds either a delay in seconds or an HTTP date.
    /// </summary>
    /// <param name="response">The response, or <see langword="null"/> when there is none.</param>
    /// <param name="now">The current time, from which a delay is counted.</param>
    /// <returns>The time the request may be retried, or <see langword="null"/> when the header is missing or invalid.</returns>
    private static DateTimeOffset? ReadRetryAfter(IResponse? response, DateTimeOffset now)
    {
        var value = response?.Headers
            .FirstOrDefault(header => header.Key.Equals(_retryAfterHeader, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return now.AddSeconds(seconds);
        }

        return DateTimeOffset.TryParseExact(
            value, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }
}
