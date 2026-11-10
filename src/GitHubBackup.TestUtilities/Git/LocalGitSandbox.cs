namespace GitHubBackup.TestUtilities.Git;

/// <summary>
/// Owns a temporary root folder in which tests build local bare repositories (the "server") with working clones, and
/// deletes everything on dispose. All git commands run isolated from the machine's git configuration.
/// </summary>
public sealed class LocalGitSandbox : IDisposable
{
    private readonly GitCommandRunner _runner;

    /// <summary>
    /// Creates the root folder and the empty global git configuration.
    /// </summary>
    /// <param name="gitPath">The path of <c>git.exe</c>, or <see langword="null"/> to use the first one on <c>PATH</c>.</param>
    public LocalGitSandbox(string? gitPath = null)
    {
        RootPath = TemporaryFolder.Create();
        var globalConfigPath = Path.Combine(RootPath, ".gitconfig");
        File.WriteAllText(globalConfigPath, string.Empty);
        _runner = new GitCommandRunner(gitPath ?? GitCommandRunner.LocateGit(), globalConfigPath);
    }

    /// <summary>Gets the absolute path of the root folder.</summary>
    public string RootPath { get; }

    /// <summary>
    /// Creates a bare repository whose <c>main</c> branch holds one initial commit.
    /// </summary>
    /// <param name="name">The name of the repository, unique within the sandbox.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The remote with its working clone.</returns>
    public async Task<LocalGitRemote> CreateRemoteAsync(string name, CancellationToken cancellationToken)
    {
        var remote = await CreateEmptyRemoteAsync(name, cancellationToken);
        await remote.CommitAsync("README.md", name, "Initial commit", cancellationToken);
        return remote;
    }

    /// <summary>
    /// Creates a bare repository without any commit.
    /// </summary>
    /// <param name="name">The name of the repository, unique within the sandbox.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The remote with its working clone.</returns>
    public async Task<LocalGitRemote> CreateEmptyRemoteAsync(string name, CancellationToken cancellationToken)
    {
        var barePath = Path.Combine(RootPath, "remotes", name + ".git");
        var workingPath = Path.Combine(RootPath, "clones", name);
        Directory.CreateDirectory(barePath);
        Directory.CreateDirectory(workingPath);

        await _runner.RunAsync(barePath, cancellationToken, "init", "--bare", "-b", "main");
        await _runner.RunAsync(workingPath, cancellationToken, "init", "-b", "main");

        var remote = new LocalGitRemote(_runner, barePath, workingPath);
        await _runner.RunAsync(workingPath, cancellationToken, "remote", "add", "origin", remote.Url);
        return remote;
    }

    /// <summary>
    /// Creates an empty folder inside the sandbox, for example a clone folder for the code under test.
    /// </summary>
    /// <param name="name">The name of the folder.</param>
    /// <returns>The absolute path of the folder.</returns>
    public string CreateFolder(string name)
    {
        var path = Path.Combine(RootPath, "folders", name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Runs any git command in the isolated environment.
    /// </summary>
    /// <param name="workingDirectory">The folder the command runs in.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The non-empty lines of standard output.</returns>
    /// <exception cref="InvalidOperationException">The command fails; the message carries the command, the exit code
    /// and the standard error.</exception>
    public Task<IReadOnlyList<string>> RunGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        _runner.RunAsync(workingDirectory, cancellationToken, arguments);

    /// <summary>
    /// Deletes the root folder and everything in it. Calling it again has no effect.
    /// </summary>
    public void Dispose() => TemporaryFolder.Delete(RootPath);
}
