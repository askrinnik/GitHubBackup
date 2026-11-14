using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Serves the part of the GitHub REST API that a backup run reads, through WireMock.Net on the loopback interface.
/// Every request that no scenario method has set up is answered with 404, as GitHub answers an unknown resource.
/// </summary>
public sealed class GitHubApiStub : IDisposable
{
    /// <summary>The priority of the responses the scenario methods set up; a lower number wins in WireMock.Net.</summary>
    private const int _scenarioPriority = 1;

    /// <summary>The priority of the 404 answer to every other request.</summary>
    private const int _fallbackPriority = 100;

    /// <summary>
    /// The identifier of the <c>/user/repos</c> response, so that setting it up again replaces the previous one.
    /// </summary>
    private static readonly Guid _currentUserRepositoriesMapping = new("4f6a3c1e-0b5d-4e57-9a2f-7c1d8e6b3a90");

    private readonly WireMockServer _server = WireMockServer.Start();

    /// <summary>
    /// Starts the server with an empty list of the token's repositories and a 404 for every other request.
    /// </summary>
    public GitHubApiStub()
    {
        GivenCurrentUserRepositories();
        _server
            .Given(Request.Create().WithPath(_ => true))
            .AtPriority(_fallbackPriority)
            .RespondWith(Json(404, """{ "message": "Not Found", "documentation_url": "https://docs.github.com/rest" }"""));
    }

    /// <summary>Gets the base address of the API, for the <c>GitHub:ApiBaseUrl</c> setting.</summary>
    public string Url => _server.Url!;

    /// <summary>
    /// Builds the JSON of one repository as GitHub returns it, public, without a wiki and with <c>main</c> as its
    /// default branch.
    /// </summary>
    /// <param name="id">The repository id.</param>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The JSON object.</returns>
    public static string Repository(long id, string owner, string name) =>
        $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "full_name": "{{owner}}/{{name}}",
          "owner": { "login": "{{owner}}", "id": 7, "type": "User" },
          "private": false,
          "fork": false,
          "archived": false,
          "has_wiki": false,
          "default_branch": "main",
          "html_url": "https://github.com/{{owner}}/{{name}}"
        }
        """;

    /// <summary>
    /// Serves the list of the token's repositories, replacing the list served before.
    /// </summary>
    /// <param name="repositories">The repository JSON objects, built with <see cref="Repository"/>.</param>
    public void GivenCurrentUserRepositories(params string[] repositories) =>
        _server
            .Given(Request.Create().WithPath("/user/repos").UsingGet())
            .WithGuid(_currentUserRepositoriesMapping)
            .AtPriority(_scenarioPriority)
            .RespondWith(Json(200, JsonArray(repositories)));

    /// <summary>
    /// Serves one repository by its owner and name and by its id.
    /// </summary>
    /// <param name="id">The repository id.</param>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The JSON object served, for use in a list.</returns>
    public string GivenRepository(long id, string owner, string name)
    {
        var repository = Repository(id, owner, name);
        GivenGet($"/repos/{owner}/{name}", repository);
        GivenGet($"/repositories/{id}", repository);
        return repository;
    }

    /// <summary>
    /// Serves a user account and the list of its repositories.
    /// </summary>
    /// <param name="login">The account login.</param>
    /// <param name="repositories">The repository JSON objects, built with <see cref="Repository"/>.</param>
    public void GivenUserAccount(string login, params string[] repositories)
    {
        GivenGet($"/users/{login}", $$"""{ "login": "{{login}}", "id": 9, "type": "User" }""");
        GivenGet($"/users/{login}/repos", JsonArray(repositories));
    }

    /// <summary>
    /// Stops the server.
    /// </summary>
    public void Dispose() => _server.Dispose();

    /// <summary>
    /// Builds a JSON response with <paramref name="statusCode"/>.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="body">The JSON body.</param>
    /// <returns>The response.</returns>
    private static IResponseBuilder Json(int statusCode, string body) =>
        Response.Create()
            .WithStatusCode(statusCode)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);

    /// <summary>
    /// Joins JSON objects into a JSON array.
    /// </summary>
    /// <param name="items">The JSON objects.</param>
    /// <returns>The JSON array.</returns>
    private static string JsonArray(IEnumerable<string> items) => $"[{string.Join(",", items)}]";

    /// <summary>
    /// Answers a <c>GET</c> of <paramref name="path"/> with 200 and <paramref name="body"/>.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="body">The JSON body.</param>
    private void GivenGet(string path, string body) =>
        _server
            .Given(Request.Create().WithPath(path).UsingGet())
            .AtPriority(_scenarioPriority)
            .RespondWith(Json(200, body));
}
