using System.IO.Abstractions;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.Processes;
using GitHubBackup.Infrastructure.Tools;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Authentication;

/// <summary>
/// Asks the GitHub CLI for the token of the account it is logged in to, with <c>gh auth token</c>.
/// </summary>
/// <remarks>
/// The token travels on the standard output of <c>gh</c>, so the request marks that stream secret and it is never
/// logged. A missing, failing or logged-out <c>gh</c> is not an error: the chain moves on to the next source.
/// </remarks>
/// <param name="processRunner">Runs <c>gh</c>.</param>
/// <param name="fileSystem">Checks the folders of <c>PATH</c>.</param>
/// <param name="logger">Logs why no token was found.</param>
internal sealed class GitHubCliTokenSource(
    IProcessRunner processRunner, IFileSystem fileSystem, ILogger<GitHubCliTokenSource> logger) : ITokenSource
{
    /// <summary>The name of the GitHub CLI executable searched for in <c>PATH</c>.</summary>
    internal const string FileName = "gh.exe";

    /// <summary>The arguments that print the token for github.com.</summary>
    internal static readonly IReadOnlyList<string> Arguments = ["auth", "token", "--hostname", "github.com"];

    /// <summary>
    /// The variables that keep <c>gh</c> from asking a question or checking for a newer version, so it never waits for
    /// input and never reaches the network.
    /// </summary>
    internal static readonly IReadOnlyList<ProcessEnvironmentVariable> EnvironmentVariables =
    [
        new("GH_PROMPT_DISABLED", "1"),
        new("GH_NO_UPDATE_NOTIFIER", "1"),
    ];

    /// <summary>Gets the function that reads an environment variable; tests replace it to supply <c>PATH</c>.</summary>
    internal Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    /// <inheritdoc />
    public string Name => "GitHub CLI (gh auth token)";

    /// <inheritdoc />
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var path = ExecutableSearchPath.Find(fileSystem, GetEnvironmentVariable("PATH"), FileName);
        if (path is null)
        {
            TokenSourceLog.GitHubCliNotFound(logger, FileName);
            return null;
        }

        var request = new ProcessRequest
        {
            FileName = path,
            Arguments = Arguments,
            WorkingDirectory = fileSystem.Path.GetDirectoryName(path)!,
            EnvironmentVariables = EnvironmentVariables,
            IsStandardOutputSecret = true,
        };

        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(request, cancellationToken);
        }
        catch (ProcessStartException exception)
        {
            TokenSourceLog.GitHubCliStartFailed(logger, path, exception.Message);
            return null;
        }

        var token = result.Succeeded
            ? result.StandardOutput.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
            : null;
        if (token is null)
        {
            TokenSourceLog.GitHubCliNoToken(logger, result.ExitCode);
        }

        return token;
    }
}
