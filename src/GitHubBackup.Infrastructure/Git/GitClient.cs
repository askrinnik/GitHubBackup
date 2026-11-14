using System.Globalization;
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

    /// <summary>The exit code of <c>git config --get</c> when the key is not set.</summary>
    private const int _configKeyMissingExitCode = 1;

    /// <summary>The index mode of a submodule.</summary>
    private const string _gitlinkMode = "160000";

    /// <summary>The index mode of a symbolic link.</summary>
    private const string _symbolicLinkMode = "120000";

    /// <summary>The most remote-tracking commits passed on one command line when unpushed commits are counted.</summary>
    private const int _maxKnownRemoteCommits = 200;

    /// <summary>The deepest nesting of submodules that is listed.</summary>
    private const int _maxSubmoduleDepth = 8;

    /// <summary>
    /// The name of the folder inside <c>.git</c> that git is told to look in for hooks; the application never creates
    /// it, so no hook of the repository or of the user runs in the application's git processes.
    /// </summary>
    private const string _missingHooksFolderName = "githubbackup-no-hooks";

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

    /// <summary>The arguments that read the URL of <c>origin</c> from the clone's configuration.</summary>
    private static readonly string[] _originUrlArguments = ["config", "--get", "remote.origin.url"];

    /// <summary>The arguments that read the branch, the changes and the untracked files without taking locks.</summary>
    private static readonly string[] _statusArguments =
        ["--no-optional-locks", "status", "--porcelain=v2", "--branch", "--untracked-files=normal", "--ignore-submodules=none"];

    /// <summary>
    /// The arguments that list the ignored files of a repository folder, a wholly ignored folder as one entry.
    /// </summary>
    private static readonly string[] _ignoredArguments =
        ["ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--directory"];

    /// <summary>The arguments that list the entries of the index with their modes.</summary>
    private static readonly string[] _stageArguments = ["ls-files", "-z", "--stage"];

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

        var environments = CreateEnvironments(request.Token, request.ClonePath);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var maskedUrl = secretMasker.Mask(remoteUrl);
            GitClientLog.Cloning(logger, maskedUrl, request.ClonePath, request.Branch ?? _defaultBranchLabel);
        }

        fileSystem.Directory.CreateDirectory(parent);

        await RunGitAsync(
            request,
            parent,
            environments.Remote,
            ["clone", "--no-checkout", "--origin", "origin", "--", remoteUrl, request.ClonePath],
            cancellationToken);
        return await SynchronizeAsync(request, environments, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GitSyncResult> UpdateAsync(GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        await FetchAsync(request, cancellationToken);
        return await SyncAsync(request, requiredBranch: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task FetchAsync(GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        GitClientLog.Updating(logger, request.ClonePath, request.Branch ?? _defaultBranchLabel);
        await RunGitAsync(
            request,
            request.ClonePath,
            CreateEnvironment(request.Token, request.ClonePath),
            ["fetch", "origin", "--prune", "--prune-tags", "--tags", "--force"],
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GitSyncResult> SyncAsync(
        GitRepositoryRequest request, string? requiredBranch, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        if (requiredBranch is not null)
        {
            ValidateBranch(requiredBranch, request);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var environments = CreateEnvironments(request.Token, request.ClonePath);
        return requiredBranch is null
            ? SynchronizeAsync(request, environments, cancellationToken)
            : SynchronizeToAsync(request, requiredBranch, environments, cancellationToken);
    }

    /// <summary>
    /// Brings the working tree to a branch chosen beforehand, failing instead of choosing another one when the fetch
    /// removed it.
    /// </summary>
    /// <param name="request">The clone to synchronize.</param>
    /// <param name="branch">The branch to check out.</param>
    /// <param name="environments">The environments of the local steps and of the steps that talk to the server.</param>
    /// <param name="cancellationToken">Cancels the remaining steps.</param>
    /// <returns>The branch checked out and the refs of the clone.</returns>
    /// <exception cref="GitBranchNotFoundException">The fetched refs no longer hold the branch.</exception>
    private async Task<GitSyncResult> SynchronizeToAsync(
        GitRepositoryRequest request, string branch, GitEnvironments environments, CancellationToken cancellationToken)
    {
        var refs = await ReadLocalRefsAsync(request, environments.Local, cancellationToken);
        if (!refs.ContainsBranch(branch))
        {
            throw new GitBranchNotFoundException(
                $"Branch '{branch}' was checked before the fetch, but the server of '{request.ClonePath}' no longer has it.",
                request.Branch,
                branch);
        }

        await SyncWorkingTreeAsync(request, branch, environments, cancellationToken);
        return new(branch, request.Branch is not null && !string.Equals(request.Branch, branch, StringComparison.Ordinal), refs);
    }

    /// <summary>
    /// Rejects a request whose clone path is not fully qualified or whose branch git could not use.
    /// </summary>
    /// <param name="request">The request to check.</param>
    /// <exception cref="ArgumentException">The clone path or the branch is not valid.</exception>
    private static void ValidateRequest(GitRepositoryRequest request)
    {
        ValidateClonePath(request);
        if (request.Branch is not null)
        {
            ValidateBranch(request.Branch, request);
        }
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

    /// <inheritdoc />
    public async Task<string?> GetOriginUrlAsync(GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await RunGitProcessAsync(
            request,
            request.ClonePath,
            CreateEnvironment(token: null, request.ClonePath),
            _originUrlArguments,
            cancellationToken);
        if (result.ExitCode == _configKeyMissingExitCode)
        {
            return null;
        }

        EnsureSucceeded(request, _originUrlArguments, result);
        return result.StandardOutput.FirstOrDefault(line => line.Length > 0)?.Trim();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>status</c> runs with <c>--no-optional-locks</c>, so it does not refresh the index of a working copy the user
    /// may be using at the same moment, and with <c>--ignore-submodules=none</c>, so a setting of the user cannot hide a
    /// changed submodule.
    /// </remarks>
    public async Task<WorkingCopyStatus> GetWorkingCopyStatusAsync(
        GitRepositoryRequest request, RefSnapshot? knownRemoteRefs, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        cancellationToken.ThrowIfCancellationRequested();

        var environment = CreateEnvironment(token: null, request.ClonePath);
        var output = await RunGitAsync(request, request.ClonePath, environment, _statusArguments, cancellationToken);
        var status = ParseOutput(request, _statusArguments, output, GitStatusParser.Parse);
        var unpushed = status.HasCommits
            ? await CountUnpushedCommitsAsync(request, "HEAD", status.Branch, knownRemoteRefs, environment, cancellationToken)
            : 0;
        return new(status.Branch, status.HasCommits, status.HasChanges, status.HasUntrackedFiles, unpushed);
    }

    /// <inheritdoc />
    public async Task<int> GetUnpushedBranchCommitsAsync(
        GitRepositoryRequest request, string branch, RefSnapshot? knownRemoteRefs, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        ValidateBranch(branch, request);
        cancellationToken.ThrowIfCancellationRequested();

        var environment = CreateEnvironment(token: null, request.ClonePath);
        var localRef = "refs/heads/" + branch;
        var exists = await RunGitProcessAsync(
            request, request.ClonePath, environment, ["rev-parse", "--verify", "--quiet", localRef], cancellationToken);
        return exists.Succeeded
            ? await CountUnpushedCommitsAsync(request, localRef, branch, knownRemoteRefs, environment, cancellationToken)
            : 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetIgnoredPathsAsync(
        GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.ClonePath));
        var paths = new List<string>();
        foreach (var folder in await ListRepositoryFoldersAsync(request, root, cancellationToken))
        {
            var ignored = await RunGitAsync(
                request, folder.Path, CreateEnvironment(token: null, folder.Path), _ignoredArguments, cancellationToken);
            foreach (var path in SplitNulSeparated(ignored))
            {
                if (TryResolveInside(root, folder.Prefix, path) is { } relative)
                {
                    paths.Add(relative);
                }
            }
        }

        return paths;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetSymbolicLinkPathsAsync(
        GitRepositoryRequest request, CancellationToken cancellationToken)
    {
        ValidateClonePath(request);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.ClonePath));
        var folders = await ListRepositoryFoldersAsync(request, root, cancellationToken);
        return [.. folders.SelectMany(folder => folder.Entries).Where(entry => entry.Mode == _symbolicLinkMode).Select(entry => entry.Path)];
    }

    /// <summary>
    /// Counts the commits of <paramref name="revision"/> the clone has not seen on <c>origin</c>, from the
    /// remote-tracking branches it already has or had.
    /// </summary>
    /// <param name="request">The clone.</param>
    /// <param name="revision">The commit to count from: <c>HEAD</c> or the full name of a local branch.</param>
    /// <param name="branch">The branch whose remote-tracking branch is compared, or <see langword="null"/>.</param>
    /// <param name="knownRemoteRefs">
    /// The remote-tracking branches as they were before a fetch, or <see langword="null"/> to use the current ones.
    /// </param>
    /// <param name="environment">The environment of the git processes.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <returns>
    /// The commits not in <c>origin/&lt;branch&gt;</c> when the clone has that branch, otherwise the commits in no
    /// remote-tracking branch of <c>origin</c>.
    /// </returns>
    private async Task<int> CountUnpushedCommitsAsync(
        GitRepositoryRequest request,
        string revision,
        string? branch,
        RefSnapshot? knownRemoteRefs,
        IReadOnlyList<ProcessEnvironmentVariable> environment,
        CancellationToken cancellationToken)
    {
        var arguments = knownRemoteRefs is null
            ? await CurrentRefsCountArgumentsAsync(request, revision, branch, environment, cancellationToken)
            : KnownRefsCountArguments(revision, branch, knownRemoteRefs);
        var output = await RunGitAsync(request, request.ClonePath, environment, arguments, cancellationToken);
        return ParseOutput(request, arguments, output, ParseCount);
    }

    /// <summary>
    /// Builds the arguments that count against the remote-tracking branches the clone has now.
    /// </summary>
    /// <param name="request">The clone.</param>
    /// <param name="revision">The commit to count from.</param>
    /// <param name="branch">The branch whose remote-tracking branch is compared, or <see langword="null"/>.</param>
    /// <param name="environment">The environment of the git processes.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The <c>rev-list --count</c> arguments.</returns>
    private async Task<string[]> CurrentRefsCountArgumentsAsync(
        GitRepositoryRequest request,
        string revision,
        string? branch,
        IReadOnlyList<ProcessEnvironmentVariable> environment,
        CancellationToken cancellationToken)
    {
        if (branch is not null)
        {
            // The full ref name keeps a branch that starts with '-' from being read as an option.
            var trackingRef = "refs/remotes/origin/" + branch;
            var exists = await RunGitProcessAsync(
                request,
                request.ClonePath,
                environment,
                ["rev-parse", "--verify", "--quiet", trackingRef],
                cancellationToken);
            if (exists.Succeeded)
            {
                return ["rev-list", "--count", $"{trackingRef}..{revision}", "--"];
            }
        }

        return ["rev-list", "--count", revision, "--not", "--remotes=origin", "--"];
    }

    /// <summary>
    /// Builds the arguments that count against the commits the remote-tracking branches pointed at before a fetch,
    /// so commits the server dropped by a force-push do not count as the user's work.
    /// </summary>
    /// <remarks>
    /// The commit ids come from <c>for-each-ref</c> and are hexadecimal, so none can be read as an option. Beyond
    /// <see cref="_maxKnownRemoteCommits"/> branches the command line could grow too long, so the current
    /// remote-tracking branches stand in.
    /// </remarks>
    /// <param name="revision">The commit to count from.</param>
    /// <param name="branch">The branch whose remote-tracking branch is compared, or <see langword="null"/>.</param>
    /// <param name="knownRemoteRefs">The remote-tracking branches and tags before the fetch.</param>
    /// <returns>The <c>rev-list --count</c> arguments.</returns>
    private static string[] KnownRefsCountArguments(string revision, string? branch, RefSnapshot knownRemoteRefs)
    {
        if (branch is not null && knownRemoteRefs.Refs.TryGetValue(RefSnapshot.BranchPrefix + branch, out var tracking))
        {
            return ["rev-list", "--count", revision, "--not", tracking, "--"];
        }

        var commits = knownRemoteRefs.Refs
            .Where(entry => entry.Key.StartsWith(RefSnapshot.BranchPrefix, StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return commits.Count switch
        {
            0 => ["rev-list", "--count", revision, "--"],
            > _maxKnownRemoteCommits => ["rev-list", "--count", revision, "--not", "--remotes=origin", "--"],
            _ => ["rev-list", "--count", revision, "--not", .. commits, "--"],
        };
    }

    /// <summary>
    /// Reads the single number <c>git rev-list --count</c> writes.
    /// </summary>
    /// <param name="lines">The lines of standard output.</param>
    /// <returns>The number.</returns>
    /// <exception cref="FormatException">The output is not one non-negative number.</exception>
    private static int ParseCount(IReadOnlyList<string> lines) =>
        lines.FirstOrDefault(line => line.Length > 0) is { } line
            && int.TryParse(line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : throw new FormatException("rev-list --count wrote no number.");

    /// <summary>
    /// Lists the clone and every checked-out submodule in it, recursively, with the entries of the index of each.
    /// </summary>
    /// <remarks>
    /// Submodules come from the gitlinks of the index, not from <c>.gitmodules</c>, which the repository controls. A
    /// submodule is followed only when its folder lies strictly inside the folder that lists it and holds <c>.git</c>,
    /// each folder is listed once, and the nesting stops at <see cref="_maxSubmoduleDepth"/>, so a crafted path cannot
    /// make the walk loop or leave the clone.
    /// </remarks>
    /// <param name="request">The request that supplies the git executable.</param>
    /// <param name="root">The full path of the clone, which every returned path stays inside.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <returns>The repository folders, the clone first.</returns>
    private async Task<List<RepositoryFolder>> ListRepositoryFoldersAsync(
        GitRepositoryRequest request, string root, CancellationToken cancellationToken)
    {
        var folders = new List<RepositoryFolder>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(string Prefix, int Depth)>([(string.Empty, 0)]);
        while (pending.TryDequeue(out var next))
        {
            var (prefix, depth) = next;
            var folder = prefix.Length == 0 ? root : Path.Combine(root, prefix);
            if (!visited.Add(folder))
            {
                continue;
            }

            var output = await RunGitAsync(
                request, folder, CreateEnvironment(token: null, folder), _stageArguments, cancellationToken);
            var entries = new List<(string Mode, string Path)>();
            foreach (var line in SplitNulSeparated(output))
            {
                // Each entry is "<mode> <object> <stage>", a tab and the path.
                var tab = line.IndexOf('\t', StringComparison.Ordinal);
                var space = line.IndexOf(' ', StringComparison.Ordinal);
                if (tab < 0 || space < 0 || space > tab || TryResolveInside(root, prefix, line[(tab + 1)..]) is not { } path)
                {
                    continue;
                }

                var mode = line[..space];
                entries.Add((mode, path));
                if (mode == _gitlinkMode
                    && depth < _maxSubmoduleDepth
                    && (prefix.Length == 0 || path.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    && (fileSystem.Directory.Exists(Path.Combine(root, path, ".git"))
                        || fileSystem.File.Exists(Path.Combine(root, path, ".git"))))
                {
                    pending.Enqueue((path, depth + 1));
                }
            }

            folders.Add(new(prefix, folder, entries));
        }

        return folders;
    }

    /// <summary>
    /// Splits output written with <c>-z</c> into its entries.
    /// </summary>
    /// <param name="lines">The lines of standard output; a line break inside an entry splits it into lines.</param>
    /// <returns>The non-empty entries.</returns>
    private static string[] SplitNulSeparated(IReadOnlyList<string> lines) =>
        string.Join('\n', lines).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Turns a path git reported relative to a repository folder into a path relative to the clone, rejecting a path
    /// that would leave the clone.
    /// </summary>
    /// <param name="root">The full path of the clone.</param>
    /// <param name="prefix">The repository folder relative to <paramref name="root"/>, empty for the clone.</param>
    /// <param name="path">The path git reported, with <c>/</c> as the separator.</param>
    /// <returns>The path relative to the clone with <c>\</c> as the separator, or <see langword="null"/>.</returns>
    private static string? TryResolveInside(string root, string prefix, string path)
    {
        var relative = Path.TrimEndingDirectorySeparator(path.Replace('/', Path.DirectorySeparatorChar));
        if (relative.Length == 0 || Path.IsPathRooted(relative))
        {
            return null;
        }

        var combined = prefix.Length == 0 ? relative : Path.Combine(prefix, relative);
        var full = Path.GetFullPath(Path.Combine(root, combined));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full[(root.Length + 1)..]
            : null;
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
    /// Registers the token with the masker and builds the environment of git processes that work on one folder.
    /// </summary>
    /// <param name="token">
    /// The GitHub token for a process that talks to the server, or <see langword="null"/> for anonymous access and for
    /// every process that only works on the local repository.
    /// </param>
    /// <param name="clonePath">
    /// The clone folder; its parent, taken from the normalized path so that a trailing separator or <c>.</c> cannot
    /// turn the folder itself into the ceiling, becomes the ceiling of git's repository discovery.
    /// </param>
    /// <param name="skipLfsSmudge">Whether the git-lfs filter leaves pointer files in place.</param>
    /// <returns>The environment variables of the git processes.</returns>
    private IReadOnlyList<ProcessEnvironmentVariable> CreateEnvironment(
        string? token, string clonePath, bool skipLfsSmudge = false)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            secretMasker.Register(token);
            secretMasker.Register(GitEnvironment.EncodeCredentials(token));
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(clonePath));
        return GitEnvironment.Create(
            token,
            AdditionalConfiguration,
            Path.GetDirectoryName(fullPath),
            Path.Combine(fullPath, ".git", _missingHooksFolderName),
            skipLfsSmudge);
    }

    /// <summary>
    /// Builds the two environments of an operation: one with the token for the steps that talk to the server, one
    /// without it for the steps that only change the local repository and its working tree.
    /// </summary>
    /// <remarks>
    /// Local steps such as checkout and reset start filters and other programs that the repository or the working
    /// copy configures, so they never see the authorization header. Without it the git-lfs filter could not download
    /// from a private repository, so it leaves pointer files, and the later <c>git lfs pull</c>, which has the token,
    /// replaces them for the checked-out branch.
    /// </remarks>
    /// <param name="token">The GitHub token, or <see langword="null"/> for anonymous access.</param>
    /// <param name="clonePath">The clone folder.</param>
    /// <returns>The environments.</returns>
    private GitEnvironments CreateEnvironments(string? token, string clonePath) =>
        new(CreateEnvironment(token: null, clonePath, skipLfsSmudge: true), CreateEnvironment(token, clonePath));

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
    /// <param name="environments">The environments of the local steps and of the steps that talk to the server.</param>
    /// <param name="cancellationToken">Cancels the remaining steps.</param>
    /// <returns>The branch checked out and the refs of the clone.</returns>
    /// <exception cref="GitBranchNotFoundException">No existing branch can be chosen.</exception>
    private async Task<GitSyncResult> SynchronizeAsync(
        GitRepositoryRequest request,
        GitEnvironments environments,
        CancellationToken cancellationToken)
    {
        // The steps after this read change only the working tree and the local branch, never the remote-tracking
        // branches or the tags, so this snapshot is also the state after the synchronization.
        var refs = await ReadLocalRefsAsync(request, environments.Local, cancellationToken);
        if (!refs.HasBranches)
        {
            GitClientLog.EmptyRepository(logger, request.ClonePath);
            return new(null, false, refs);
        }

        if (request.Branch is not null && refs.ContainsBranch(request.Branch))
        {
            await SyncWorkingTreeAsync(request, request.Branch, environments, cancellationToken);
            return new(request.Branch, false, refs);
        }

        var defaultBranch = await ReadDefaultBranchAsync(request, environments.Remote, cancellationToken);
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

        await SyncWorkingTreeAsync(request, defaultBranch, environments, cancellationToken);
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
    /// except the ignored files when <see cref="GitRepositoryRequest.KeepIgnoredFiles"/> is set, and brings the
    /// submodules and the LFS files in line.
    /// </summary>
    /// <param name="request">The clone to synchronize.</param>
    /// <param name="branch">The branch to check out; it exists on <c>origin</c> and does not start with <c>-</c>.</param>
    /// <param name="environments">The environments of the local steps and of the steps that talk to the server.</param>
    /// <param name="cancellationToken">Cancels the remaining steps.</param>
    /// <returns>A task that completes when the working tree matches the server.</returns>
    private async Task SyncWorkingTreeAsync(
        GitRepositoryRequest request,
        string branch,
        GitEnvironments environments,
        CancellationToken cancellationToken)
    {
        var remoteBranch = "origin/" + branch;
        (string[] Arguments, bool Remote)[] steps =
        [
            (["checkout", "-B", branch, remoteBranch, "--"], false),
            (["reset", "--hard", remoteBranch, "--"], false),
            (request.KeepIgnoredFiles ? ["clean", "-ffd"] : ["clean", "-ffdx"], false),
            (["submodule", "sync", "--recursive"], false),
            (["submodule", "update", "--init", "--recursive", "--force"], true),
            (["lfs", "pull"], true),
        ];

        foreach (var (arguments, remote) in steps)
        {
            await RunGitAsync(
                request, request.ClonePath, remote ? environments.Remote : environments.Local, arguments, cancellationToken);
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
        var result = await RunGitProcessAsync(request, workingDirectory, environment, arguments, cancellationToken);
        EnsureSucceeded(request, arguments, result);
        return result.StandardOutput;
    }

    /// <summary>
    /// Runs one git command and returns its result whatever the exit code.
    /// </summary>
    /// <param name="request">The request that supplies the git executable.</param>
    /// <param name="workingDirectory">The folder git runs in.</param>
    /// <param name="environment">The environment of the git process.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code and the output of the command.</returns>
    private Task<ProcessResult> RunGitProcessAsync(
        GitRepositoryRequest request,
        string workingDirectory,
        IReadOnlyList<ProcessEnvironmentVariable> environment,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            new()
            {
                FileName = request.GitPath,
                WorkingDirectory = workingDirectory,
                Arguments = arguments,
                EnvironmentVariables = environment,
            },
            cancellationToken);

    /// <summary>
    /// Throws when a git command exited with a non-zero code.
    /// </summary>
    /// <param name="request">The request that names the clone folder.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="result">The result of the command.</param>
    /// <exception cref="GitRepositoryNotFoundException">The command fails because the remote URL has no repository.</exception>
    /// <exception cref="GitCommandException">The command exits with a non-zero code.</exception>
    private void EnsureSucceeded(GitRepositoryRequest request, IReadOnlyList<string> arguments, ProcessResult result)
    {
        if (result.Succeeded)
        {
            return;
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

    /// <summary>
    /// Holds the environment of the local steps of one operation and the environment of its steps that talk to the
    /// server.
    /// </summary>
    /// <param name="Local">The environment without the token.</param>
    /// <param name="Remote">The environment with the token, when there is one.</param>
    private readonly record struct GitEnvironments(
        IReadOnlyList<ProcessEnvironmentVariable> Local, IReadOnlyList<ProcessEnvironmentVariable> Remote);

    /// <summary>
    /// Describes the clone or one of its checked-out submodules.
    /// </summary>
    /// <param name="Prefix">The folder relative to the clone, empty for the clone itself.</param>
    /// <param name="Path">The full path of the folder.</param>
    /// <param name="Entries">The mode of each entry of its index and its path relative to the clone.</param>
    private sealed record RepositoryFolder(string Prefix, string Path, List<(string Mode, string Path)> Entries);
}
