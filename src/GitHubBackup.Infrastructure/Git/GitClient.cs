using System.IO.Abstractions;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Processes;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Git;

/// <summary>
/// Clones and updates repositories and reads their refs by running git through <see cref="IProcessRunner"/>, one
/// process per step.
/// </summary>
/// <remarks>
/// A clone is made with <c>--no-checkout</c> and then brought to the branch by the same steps as an update, so both
/// paths end in the same state and LFS files are fetched only for the checked-out branch.
/// </remarks>
/// <param name="processRunner">Runs git.</param>
/// <param name="fileSystem">Creates the parent folder of a new clone.</param>
/// <param name="secretMasker">Learns the token in its raw and encoded forms and masks failure messages.</param>
/// <param name="logger">Logs the start of each operation and the choice of the branch.</param>
internal sealed class GitClient(
    IProcessRunner processRunner, IFileSystem fileSystem, ISecretMasker secretMasker, ILogger<GitClient> logger)
    : IGitClient
{
    /// <summary>The number of standard error lines quoted in the message of a <see cref="GitCommandException"/>.</summary>
    private const int _quotedErrorLines = 5;

    /// <summary>The text logged as the branch when the request leaves the choice to the server.</summary>
    private const string _defaultBranchLabel = "(server default)";

    /// <summary>The exit code git uses for a fatal error.</summary>
    private const int _fatalExitCode = 128;

    /// <summary>The sentence GitHub sends when the remote URL has no repository.</summary>
    private const string _serverNotFoundMarker = "Repository not found.";

    /// <summary>The text git writes when a local or <c>file://</c> remote is not a repository.</summary>
    private const string _localNotFoundMarker = "does not appear to be a git repository";

    /// <summary>The text git writes when the clone has no remote named <c>origin</c>.</summary>
    private const string _missingOriginMarker = "'origin' " + _localNotFoundMarker;

    /// <summary>The arguments that list the remote-tracking branches of <c>origin</c> and the tags of the clone.</summary>
    private static readonly string[] _localRefsArguments =
        ["for-each-ref", "--format=%(objectname)%09%(refname)", "refs/remotes/origin", "refs/tags"];

    /// <summary>The arguments that list the branches and tags of the server, without peeled tags and <c>HEAD</c>.</summary>
    private static readonly string[] _remoteRefsArguments = ["ls-remote", "--refs", "origin"];

    /// <summary>The arguments that show which branch the server's <c>HEAD</c> points at.</summary>
    private static readonly string[] _defaultBranchArguments = ["ls-remote", "--symref", "origin", "HEAD"];

    /// <summary>
    /// Gets the configuration entries passed to every git process besides the fixed entries of
    /// <see cref="GitEnvironment"/> and the authorization header; tests use it to allow submodules from <c>file://</c> remotes.
    /// </summary>
    internal IReadOnlyList<KeyValuePair<string, string>> AdditionalConfiguration { get; init; } = [];

    /// <inheritdoc />
    public async Task<GitSyncResult> CloneAsync(
        GitRepositoryRequest request, string remoteUrl, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        if (request.Branch is not null)
        {
            ValidateBranch(request.Branch, request);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);
        var parent = Path.GetDirectoryName(request.ClonePath)
            ?? throw new ArgumentException($"The clone path '{request.ClonePath}' has no parent folder.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        var environment = CreateEnvironment(request.Token, request.ClonePath);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var maskedUrl = secretMasker.Mask(remoteUrl);
            GitClientLog.Cloning(logger, maskedUrl, request.ClonePath, request.Branch ?? _defaultBranchLabel);
        }

        fileSystem.Directory.CreateDirectory(parent);

        await RunGitAsync(
            request, parent, environment, ["clone", "--no-checkout", "--origin", "origin", "--", remoteUrl, request.ClonePath],
            cancellationToken);
        return await SynchronizeAsync(request, environment, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GitSyncResult> UpdateAsync(GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        if (request.Branch is not null)
        {
            ValidateBranch(request.Branch, request);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var environment = CreateEnvironment(request.Token, request.ClonePath);
        GitClientLog.Updating(logger, request.ClonePath, request.Branch ?? _defaultBranchLabel);

        await RunGitAsync(
            request, request.ClonePath, environment, ["fetch", "origin", "--prune", "--prune-tags", "--tags", "--force"],
            cancellationToken);
        return await SynchronizeAsync(request, environment, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RefSnapshot> GetRemoteRefsAsync(GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        cancellationToken.ThrowIfCancellationRequested();

        var output = await RunGitAsync(
            request, request.ClonePath, CreateEnvironment(request.Token, request.ClonePath), _remoteRefsArguments,
            cancellationToken);
        return ParseOutput(request, _remoteRefsArguments, output, GitRefParser.ParseRemoteRefs);
    }

    /// <inheritdoc />
    public Task<RefSnapshot> GetLocalRefsAsync(GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        cancellationToken.ThrowIfCancellationRequested();

        return ReadLocalRefsAsync(request, CreateEnvironment(token: null, request.ClonePath), cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> GetDefaultBranchAsync(GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        cancellationToken.ThrowIfCancellationRequested();

        return ReadDefaultBranchAsync(request, CreateEnvironment(request.Token, request.ClonePath), cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetRemoteUrlAsync(GitRepositoryRequest request, string remoteUrl, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            throw new ArgumentException($"The remote URL for '{request.ClonePath}' must not be empty.", nameof(remoteUrl));
        }

        if (remoteUrl.StartsWith('-'))
        {
            throw new ArgumentException(
                $"The remote URL for '{request.ClonePath}' must not start with '-'.", nameof(remoteUrl));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (logger.IsEnabled(LogLevel.Information))
        {
            var maskedUrl = secretMasker.Mask(remoteUrl);
            GitClientLog.SettingRemoteUrl(logger, request.ClonePath, maskedUrl);
        }

        await RunGitAsync(
            request,
            request.ClonePath,
            CreateEnvironment(token: null, request.ClonePath),
            ["remote", "set-url", "origin", remoteUrl],
            cancellationToken);
    }

    /// <summary>
    /// Rejects a clone path that is not fully qualified.
    /// </summary>
    /// <param name="request">The request to check.</param>
    /// <exception cref="ArgumentException">The clone path is not fully qualified.</exception>
    private static void ValidateClonePath(GitRepositoryRequest request)
    {
        if (!Path.IsPathFullyQualified(request.ClonePath))
        {
            throw new ArgumentException(
                $"The clone path '{request.ClonePath}' must be fully qualified.", nameof(request));
        }
    }

    /// <summary>
    /// Rejects a branch that is empty or that git could read as an option, whether the request or the server named it.
    /// </summary>
    /// <param name="branch">The branch to check.</param>
    /// <param name="request">The request the branch belongs to.</param>
    /// <exception cref="ArgumentException">The branch is empty, white space or starts with <c>-</c>.</exception>
    private static void ValidateBranch(string branch, GitRepositoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ArgumentException(
                $"The branch for '{request.ClonePath}' must not be empty.", nameof(request));
        }

        if (branch.StartsWith('-'))
        {
            throw new ArgumentException(
                $"The branch '{branch}' for '{request.ClonePath}' must not start with '-'.", nameof(request));
        }
    }

    /// <summary>
    /// Registers the token with the masker and builds the environment shared by every step of one operation.
    /// </summary>
    /// <param name="token">The GitHub token, or <see langword="null"/> for anonymous access.</param>
    /// <param name="clonePath">
    /// The clone folder; its parent, taken from the normalized path so that a trailing separator or <c>.</c> cannot
    /// turn the folder itself into the ceiling, becomes the ceiling of git's repository discovery.
    /// </param>
    /// <returns>The environment variables of the git processes.</returns>
    private IReadOnlyList<ProcessEnvironmentVariable> CreateEnvironment(string? token, string clonePath)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            secretMasker.Register(token);
            secretMasker.Register(GitEnvironment.EncodeCredentials(token));
        }

        return GitEnvironment.Create(
            token,
            AdditionalConfiguration,
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(clonePath))));
    }

    /// <summary>
    /// Parses the output of a git command and reports output that does not parse as a failed command.
    /// </summary>
    /// <typeparam name="T">The parsed type.</typeparam>
    /// <param name="request">The request that names the clone folder.</param>
    /// <param name="arguments">The git arguments that produced the output.</param>
    /// <param name="output">The lines the command wrote to standard output.</param>
    /// <param name="parse">Parses the lines; throws <see cref="FormatException"/> for malformed output.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="GitCommandException">The output is malformed.</exception>
    private T ParseOutput<T>(
        GitRepositoryRequest request,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> output,
        Func<IReadOnlyList<string>, T> parse)
    {
        try
        {
            return parse(output);
        }
        catch (FormatException exception)
        {
            var command = secretMasker.Mask(ProcessCommandLine.Format("git", arguments));
            var detail = secretMasker.Mask(exception.Message);
            throw new GitCommandException(
                $"'{command}' returned output that cannot be read for '{request.ClonePath}': {detail}",
                command,
                0);
        }
    }

    /// <summary>
    /// Chooses the branch from the refs just fetched and brings the working tree to it, or leaves the clone without a
    /// checkout when the server has no branch.
    /// </summary>
    /// <param name="request">The clone to synchronize.</param>
    /// <param name="environment">The environment of the git processes.</param>
    /// <param name="cancellationToken">Cancels the remaining steps.</param>
    /// <returns>The branch checked out and the refs of the clone.</returns>
    /// <exception cref="GitBranchNotFoundException">No existing branch can be chosen.</exception>
    private async Task<GitSyncResult> SynchronizeAsync(
        GitRepositoryRequest request,
        IReadOnlyList<ProcessEnvironmentVariable> environment,
        CancellationToken cancellationToken)
    {
        // The steps after this read change only the working tree and the local branch, never the remote-tracking
        // branches or the tags, so this snapshot is also the state after the synchronization.
        var refs = await ReadLocalRefsAsync(request, environment, cancellationToken);
        if (!refs.HasBranches)
        {
            GitClientLog.EmptyRepository(logger, request.ClonePath);
            return new(null, false, refs);
        }

        if (request.Branch is not null && refs.ContainsBranch(request.Branch))
        {
            await SyncWorkingTreeAsync(request, request.Branch, environment, cancellationToken);
            return new(request.Branch, false, refs);
        }

        var defaultBranch = await ReadDefaultBranchAsync(request, environment, cancellationToken);
        if (defaultBranch is null || !refs.ContainsBranch(defaultBranch))
        {
            var requested = request.Branch is null ? "No branch was requested" : $"Branch '{request.Branch}' does not exist";
            var serverDefault = defaultBranch is null
                ? "the server's HEAD names no existing branch"
                : $"the server's default branch '{defaultBranch}' does not exist";
            throw new GitBranchNotFoundException(
                $"{requested} on the server of '{request.ClonePath}', and {serverDefault}.",
                request.Branch,
                defaultBranch);
        }

        ValidateBranch(defaultBranch, request);
        if (request.Branch is null)
        {
            GitClientLog.UsingDefaultBranch(logger, request.ClonePath, defaultBranch);
        }
        else
        {
            GitClientLog.BranchNotFound(logger, request.Branch, request.ClonePath, defaultBranch);
        }

        await SyncWorkingTreeAsync(request, defaultBranch, environment, cancellationToken);
        return new(defaultBranch, request.Branch is not null, refs);
    }

    /// <summary>
    /// Reads the snapshot of the clone with <c>git for-each-ref</c>.
    /// </summary>
    /// <param name="request">The clone to read.</param>
    /// <param name="environment">The environment of the git process.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The remote-tracking branches and the tags of the clone.</returns>
    private async Task<RefSnapshot> ReadLocalRefsAsync(
        GitRepositoryRequest request,
        IReadOnlyList<ProcessEnvironmentVariable> environment,
        CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(request, request.ClonePath, environment, _localRefsArguments, cancellationToken);
        return ParseOutput(request, _localRefsArguments, output, GitRefParser.ParseLocalRefs);
    }

    /// <summary>
    /// Asks the server for its default branch with <c>git ls-remote --symref</c>.
    /// </summary>
    /// <param name="request">The clone whose <c>origin</c> is asked.</param>
    /// <param name="environment">The environment of the git process.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The default branch, or <see langword="null"/> when <c>HEAD</c> names no existing branch.</returns>
    private async Task<string?> ReadDefaultBranchAsync(
        GitRepositoryRequest request,
        IReadOnlyList<ProcessEnvironmentVariable> environment,
        CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(
            request, request.ClonePath, environment, _defaultBranchArguments, cancellationToken);
        return GitRefParser.ParseDefaultBranch(output);
    }

    /// <summary>
    /// Checks out <paramref name="branch"/> exactly as <c>origin</c> has it, removes every file git does not track,
    /// and brings the submodules and the LFS files in line.
    /// </summary>
    /// <param name="request">The clone to synchronize.</param>
    /// <param name="branch">The branch to check out; it exists on <c>origin</c> and does not start with <c>-</c>.</param>
    /// <param name="environment">The environment of the git processes.</param>
    /// <param name="cancellationToken">Cancels the remaining steps.</param>
    /// <returns>A task that completes when the working tree matches the server.</returns>
    private async Task SyncWorkingTreeAsync(
        GitRepositoryRequest request,
        string branch,
        IReadOnlyList<ProcessEnvironmentVariable> environment,
        CancellationToken cancellationToken)
    {
        var remoteBranch = "origin/" + branch;
        string[][] steps =
        [
            ["checkout", "-B", branch, remoteBranch, "--"],
            ["reset", "--hard", remoteBranch, "--"],
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
    /// <returns>The lines the command wrote to standard output.</returns>
    /// <exception cref="GitRepositoryNotFoundException">The command fails because the remote URL has no repository.</exception>
    /// <exception cref="GitCommandException">The command exits with a non-zero code.</exception>
    private async Task<IReadOnlyList<string>> RunGitAsync(
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
            return result.StandardOutput;
        }

        var command = secretMasker.Mask(ProcessCommandLine.Format("git", arguments));
        var error = secretMasker.Mask(string.Join(
            Environment.NewLine,
            result.StandardError.Where(line => line.Length > 0).TakeLast(_quotedErrorLines)));
        var message = $"'{command}' failed for '{request.ClonePath}' with exit code {result.ExitCode}: {error}";
        if (IsRepositoryNotFound(result.ExitCode, result.StandardError))
        {
            throw new GitRepositoryNotFoundException(message, command, result.ExitCode);
        }

        throw new GitCommandException(message, command, result.ExitCode);
    }

    /// <summary>
    /// Decides whether a failed git command reports that the remote URL has no repository.
    /// </summary>
    /// <remarks>
    /// git reports every fatal error with the same exit code, so standard error tells this case apart. GitHub answers
    /// a missing repository with a fixed, untranslated sentence; git itself reports a missing local or <c>file://</c>
    /// remote with the other marker. git writes that marker also when a damaged clone has lost its <c>origin</c>
    /// remote; that is a fault of the clone, not a missing repository, so it stays a plain failure.
    /// </remarks>
    /// <param name="exitCode">The exit code of the command.</param>
    /// <param name="standardError">The lines the command wrote to standard error.</param>
    /// <returns><see langword="true"/> when the command failed because the repository does not exist.</returns>
    private static bool IsRepositoryNotFound(int exitCode, IReadOnlyList<string> standardError) =>
        exitCode == _fatalExitCode
        && standardError.Any(line =>
            line.Contains(_serverNotFoundMarker, StringComparison.Ordinal)
            || (line.Contains(_localNotFoundMarker, StringComparison.Ordinal)
                && !line.Contains(_missingOriginMarker, StringComparison.Ordinal)));
}
