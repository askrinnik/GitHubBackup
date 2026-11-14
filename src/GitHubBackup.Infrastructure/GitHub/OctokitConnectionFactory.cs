using Octokit;
using Octokit.Internal;

namespace GitHubBackup.Infrastructure.GitHub;

/// <summary>
/// Creates the Octokit connections of the application, all bound to the configured API address.
/// </summary>
internal static class OctokitConnectionFactory
{
    /// <summary>The product name sent in the <c>User-Agent</c> header.</summary>
    private const string _productName = "GitHubBackup";

    /// <summary>
    /// Creates a connection to <paramref name="apiBaseUrl"/> that authenticates with <paramref name="credentials"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="Connection"/> keeps the base address as given; the <see cref="GitHubClient"/> constructor that
    /// takes an address would append <c>api/v3</c> to every host other than <c>api.github.com</c>.
    /// </remarks>
    /// <param name="apiBaseUrl">The configured API address.</param>
    /// <param name="credentials">The credentials of every request, or <see langword="null"/> to set them later.</param>
    /// <returns>The connection.</returns>
    public static Connection Create(string apiBaseUrl, Credentials? credentials = null) =>
        credentials is null
            ? new(CreateProductHeader(), CreateBaseAddress(apiBaseUrl))
            : new(CreateProductHeader(), CreateBaseAddress(apiBaseUrl), new InMemoryCredentialStore(credentials));

    /// <summary>
    /// Creates a connection to <paramref name="apiBaseUrl"/> that authenticates with <paramref name="credentials"/>
    /// and sends its requests through <paramref name="httpClient"/>, which the caller owns and disposes.
    /// </summary>
    /// <param name="apiBaseUrl">The configured API address.</param>
    /// <param name="credentials">The credentials of every request.</param>
    /// <param name="httpClient">The HTTP client of the connection.</param>
    /// <returns>The connection.</returns>
    public static Connection Create(string apiBaseUrl, Credentials credentials, IHttpClient httpClient) =>
        new(
            CreateProductHeader(),
            CreateBaseAddress(apiBaseUrl),
            new InMemoryCredentialStore(credentials),
            httpClient,
            new SimpleJsonSerializer());

    /// <summary>
    /// Returns the API address with a trailing slash, without which a relative resource would replace the last
    /// path segment of the address instead of being appended to it.
    /// </summary>
    /// <param name="apiBaseUrl">The configured API address.</param>
    /// <returns>The base address.</returns>
    public static Uri CreateBaseAddress(string apiBaseUrl) =>
        new(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/", UriKind.Absolute);

    /// <summary>
    /// Returns the product sent in the <c>User-Agent</c> header, with the version of this assembly.
    /// </summary>
    /// <returns>The product header.</returns>
    private static ProductHeaderValue CreateProductHeader() =>
        new(_productName, typeof(OctokitConnectionFactory).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
}
