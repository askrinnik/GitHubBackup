using System.IO.Abstractions;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Processes;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Git;

/// <summary>
/// Clones and updates repositories by running git through <see cref="IProcessRunner"/>, one process per step.
/// </summary>
/// <remarks>
/// A clone is made with <c>--no-checkout</c> and then brought to the branch by the same steps as an update, so both
/// paths end in the same state and LFS files are fetched only for the checked-out branch.
/// </remarks>
/// <param name="processRunner">Runs git.</param>
/// <param name="fileSystem">Creates the parent folder of a new clone.</param>
/// <param name="secretMasker">Learns the token in its raw and encoded forms and masks failure messages.</param>
/// <param name="logger">Logs the start of each operation.</param>
internal sealed class GitClient(
    IProcessRunner processRunner, IFileSystem fileSystem, ISecretMasker secretMasker, ILogger<GitClient> logger)
    : IGitClient
{
    /// <summary>The number of standard error lines quoted in the message of a <see cref="GitCommandException"/>.</summary>
    private const int _quotedErrorLines = 5;

    /// <summary>
    /// Gets the configuration entries passed to every git process besides <c>core.longpaths</c> and the
    /// authorization header; tests use it to allow submodules from <c>file://</c> remotes.
    /// </summary>
    internal IReadOnlyList<KeyValuePair<string, string>> AdditionalConfiguration { get; init; } = [];

    /// <inheritdoc />
    public async Task CloneAsync(GitRepositoryRequest request, string remoteUrl, CancellationToken cancellationToken)
    {
        Validate(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);
        var parent = Path.GetDirectoryName(request.ClonePath)
            ?? throw new ArgumentException($"The clone path '{request.ClonePath}' has no parent folder.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        var environment = CreateEnvironment(request.Token);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var maskedUrl = secretMasker.Mask(remoteUrl);
            GitClientLog.Cloning(logger, maskedUrl, request.ClonePath, request.Branch);
        }

        fileSystem.Directory.CreateDirectory(parent);

        await RunGitAsync(
            request, parent, environment, ["clone", "--no-checkout", "--origin", "origin", "--", remoteUrl, request.ClonePath],
            cancellationToken);
        await SyncWorkingTreeAsync(request, environment, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        var environment = CreateEnvironment(request.Token);
        GitClientLog.Updating(logger, request.ClonePath, request.Branch);

        await RunGitAsync(
            request, request.ClonePath, environment, ["fetch", "origin", "--prune", "--prune-tags", "--tags", "--force"],
            cancellationToken);
        await SyncWorkingTreeAsync(request, environment, cancellationToken);
    }

    /// <summary>
    /// Rejects a clone path that is not fully qualified and a branch that is empty or that git could read as an
    /// option.
    /// </summary>
    /// <param name="request">The request to check.</param>
    /// <exception cref="ArgumentException">
    /// The clone path is not fully qualified, or the branch is empty, white space or starts with <c>-</c>.
    /// </exception>
    private static void Validate(GitRepositoryRequest request)
    {
        if (!Path.IsPathFullyQualified(request.ClonePath))
        {
            throw new ArgumentException(
                $"The clone path '{request.ClonePath}' must be fully qualified.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Branch))
        {
            throw new ArgumentException("The branch must not be empty.", nameof(request));
        }

        if (request.Branch.StartsWith('-'))
        {
            throw new ArgumentException($"The branch '{request.Branch}' must not start with '-'.", nameof(request));
        }
    }

    /// <summary>
    /// Registers the token with the masker and builds the environment shared by every step of one operation.
    /// </summary>
    /// <param name="token">The GitHub token, or <see langword="null"/> for anonymous access.</param>
    /// <returns>The environment variables of the git processes.</returns>
    private IReadOnlyList<ProcessEnvironmentVariable> CreateEnvironment(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            secretMasker.Register(token);
            secretMasker.Register(GitEnvironment.EncodeCredentials(token));
        }

        return GitEnvironment.Create(token, AdditionalConfiguration);
    }

    /// <summary>
    /// Checks out <see cref="GitRepositoryRequest.Branch"/> exactly as <c>origin</c> has it, removes every file git
    /// does not track, and brings the submodules and the LFS files in line.
    /// </summary>
    /// <param name="request">The clone to synchronize.</param>
    /// <param name="environment">The environment of the git processes.</param>
    /// <param name="cancellationToken">Cancels the remaining steps.</param>
    /// <returns>A task that completes when the working tree matches the server.</returns>
    private async Task SyncWorkingTreeAsync(
        GitRepositoryRequest request,
        IReadOnlyList<ProcessEnvironmentVariable> environment,
        CancellationToken cancellationToken)
    {
        var remoteBranch = "origin/" + request.Branch;
        string[][] steps =
        [
            ["checkout", "-B", request.Branch, remoteBranch],
            ["reset", "--hard", remoteBranch],
            ["clean", "-ffdx"],
            ["submodule", "sync", "--recursive"],
            ["submodule", "update", "--init", "--recursive", "--force"],
            ["lfs", "pull"],
        ];

        foreach (var arguments in steps)
        {
            await RunGitAsync(request, request.ClonePath, environment, arguments, cancellationToken);
        }
    }

    /// <summary>
    /// Runs one git command and throws when it exits with a non-zero code.
    /// </summary>
    /// <param name="request">The request that supplies the git executable and the clone folder.</param>
    /// <param name="workingDirectory">The folder git runs in.</param>
    /// <param name="environment">The environment of the git process.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>A task that completes when the command has succeeded.</returns>
    /// <exception cref="GitCommandException">The command exits with a non-zero code.</exception>
    private async Task RunGitAsync(
        GitRepositoryRequest request,
        string workingDirectory,
        IReadOnlyList<ProcessEnvironmentVariable> environment,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new()
            {
                FileName = request.GitPath,
                WorkingDirectory = workingDirectory,
                Arguments = arguments,
                EnvironmentVariables = environment,
            },
            cancellationToken);

        if (result.Succeeded)
        {
            return;
        }

        var command = secretMasker.Mask(ProcessCommandLine.Format("git", arguments));
        var error = secretMasker.Mask(string.Join(
            Environment.NewLine,
            result.StandardError.Where(line => line.Length > 0).TakeLast(_quotedErrorLines)));
        throw new GitCommandException(
            $"'{command}' failed for '{request.ClonePath}' with exit code {result.ExitCode}: {error}",
            command,
            result.ExitCode);
    }
}
