namespace GitHubBackup.TestUtilities.Git;

/// <summary>
/// A bare repository acting as a server, paired with a working clone used to build its state. Every helper publishes
/// its result to the bare repository immediately.
/// </summary>
public sealed class LocalGitRemote
{
    private readonly GitCommandRunner _runner;

    /// <summary>
    /// Initializes the pair.
    /// </summary>
    /// <param name="runner">The runner for git commands.</param>
    /// <param name="barePath">The path of the bare repository.</param>
    /// <param name="workingPath">The path of the working clone.</param>
    internal LocalGitRemote(GitCommandRunner runner, string barePath, string workingPath)
    {
        _runner = runner;
        BarePath = barePath;
        WorkingPath = workingPath;
        Url = new Uri(barePath).AbsoluteUri;
    }

    /// <summary>Gets the path of the bare repository.</summary>
    public string BarePath { get; }

    /// <summary>Gets the <c>file://</c> URL of the bare repository.</summary>
    public string Url { get; }

    /// <summary>Gets the path of the working clone.</summary>
    public string WorkingPath { get; }

    /// <summary>
    /// Writes a file, commits it on the current branch and pushes the branch.
    /// </summary>
    /// <param name="fileName">The file name relative to the working clone.</param>
    /// <param name="content">The file content.</param>
    /// <param name="message">The commit message.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The id of the new commit.</returns>
    public async Task<string> CommitAsync(
        string fileName,
        string content,
        string message,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(WorkingPath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);

        await RunAsync(cancellationToken, "add", "--", fileName);
        await RunAsync(cancellationToken, "commit", "-m", message);
        await RunAsync(cancellationToken, "push", "origin", "HEAD");
        return await GetHeadAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a branch and pushes it, without switching to it.
    /// </summary>
    /// <param name="name">The branch name.</param>
    /// <param name="startPoint">The commit the branch starts at, or <see langword="null"/> for the current commit.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>A task that completes when the branch is on the server.</returns>
    public async Task CreateBranchAsync(string name, string? startPoint, CancellationToken cancellationToken)
    {
        await RunAsync(cancellationToken, startPoint is null ? ["branch", name] : ["branch", name, startPoint]);
        await RunAsync(cancellationToken, "push", "origin", name);
    }

    /// <summary>
    /// Switches the working clone to an existing branch.
    /// </summary>
    /// <param name="name">The branch name.</param>
    /// <param name="cancellationToken">Cancels the git command.</param>
    /// <returns>A task that completes when the branch is checked out.</returns>
    public Task CheckoutAsync(string name, CancellationToken cancellationToken) =>
        RunAsync(cancellationToken, "switch", name);

    /// <summary>
    /// Deletes a branch on the server and, when it exists and is not checked out, in the working clone.
    /// </summary>
    /// <param name="name">The branch name.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>A task that completes when the branch is gone from the server.</returns>
    /// <exception cref="InvalidOperationException">The branch does not exist on the server.</exception>
    public async Task DeleteBranchAsync(string name, CancellationToken cancellationToken)
    {
        await RunAsync(cancellationToken, "push", "origin", "--delete", name);
        var local = await RunAsync(cancellationToken, "branch", "--list", name);
        if (local.Count > 0)
        {
            await RunAsync(cancellationToken, "branch", "-D", name);
        }
    }

    /// <summary>
    /// Creates a tag and pushes it.
    /// </summary>
    /// <param name="name">The tag name.</param>
    /// <param name="commit">The commit to tag, or <see langword="null"/> for the current commit.</param>
    /// <param name="annotated">Whether to create an annotated tag object instead of a lightweight tag.</param>
    /// <param name="force">Whether to move the tag when it already exists, locally and on the server.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>A task that completes when the tag is on the server.</returns>
    public async Task CreateTagAsync(
        string name,
        string? commit,
        bool annotated,
        bool force,
        CancellationToken cancellationToken)
    {
        var tagArguments = new List<string> { "tag" };
        if (force)
        {
            tagArguments.Add("--force");
        }

        if (annotated)
        {
            tagArguments.AddRange(["-a", "-m", "Tag " + name]);
        }

        tagArguments.Add(name);
        if (commit is not null)
        {
            tagArguments.Add(commit);
        }

        await RunAsync(cancellationToken, [.. tagArguments]);

        var pushArguments = new List<string> { "push" };
        if (force)
        {
            pushArguments.Add("--force");
        }

        pushArguments.AddRange(["origin", "refs/tags/" + name]);
        await RunAsync(cancellationToken, [.. pushArguments]);
    }

    /// <summary>
    /// Deletes a tag on the server and in the working clone.
    /// </summary>
    /// <param name="name">The tag name.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>A task that completes when the tag is gone from the server.</returns>
    public async Task DeleteTagAsync(string name, CancellationToken cancellationToken)
    {
        await RunAsync(cancellationToken, "tag", "--delete", name);
        await RunAsync(cancellationToken, "push", "origin", "--delete", "refs/tags/" + name);
    }

    /// <summary>
    /// Moves a branch to a commit, switches the working clone to it and force-pushes it, rewriting the server history.
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <param name="commit">The commit the branch is reset to.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>A task that completes when the server branch points at <paramref name="commit"/>.</returns>
    public async Task ForcePushAsync(string branch, string commit, CancellationToken cancellationToken)
    {
        await RunAsync(cancellationToken, "switch", "--force-create", branch, commit);
        await RunAsync(cancellationToken, "push", "--force", "origin", branch);
    }

    /// <summary>
    /// Changes the branch the bare repository's <c>HEAD</c> points at, which is the server's default branch.
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <param name="cancellationToken">Cancels the git command.</param>
    /// <returns>A task that completes when <c>HEAD</c> is changed.</returns>
    public Task SetDefaultBranchAsync(string branch, CancellationToken cancellationToken) =>
        _runner.RunAsync(BarePath, cancellationToken, "symbolic-ref", "HEAD", "refs/heads/" + branch);

    /// <summary>
    /// Adds another remote as a submodule of the current branch, commits and pushes.
    /// </summary>
    /// <param name="submodule">The remote to embed.</param>
    /// <param name="path">The submodule folder relative to the working clone.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The id of the new commit.</returns>
    public async Task<string> AddSubmoduleAsync(LocalGitRemote submodule, string path, CancellationToken cancellationToken)
    {
        await RunAsync(cancellationToken, "submodule", "add", submodule.Url, path);
        await RunAsync(cancellationToken, "commit", "-m", "Add submodule " + path);
        await RunAsync(cancellationToken, "push", "origin", "HEAD");
        return await GetHeadAsync(cancellationToken);
    }

    /// <summary>
    /// Lists the refs the server advertises.
    /// </summary>
    /// <param name="cancellationToken">Cancels the git command.</param>
    /// <returns>The commit or tag object id per ref name, including <c>HEAD</c> when it resolves.</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetRemoteRefsAsync(CancellationToken cancellationToken)
    {
        var lines = await RunAsync(cancellationToken, "ls-remote", "origin");
        return lines
            .Select(line => line.Split('\t'))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
    }

    /// <summary>
    /// Runs git in the working clone.
    /// </summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The non-empty lines of standard output.</returns>
    private Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken, params string[] arguments) =>
        _runner.RunAsync(WorkingPath, cancellationToken, arguments);

    /// <summary>
    /// Returns the id of the commit the working clone has checked out.
    /// </summary>
    /// <param name="cancellationToken">Cancels the git command.</param>
    /// <returns>The commit id.</returns>
    private async Task<string> GetHeadAsync(CancellationToken cancellationToken) =>
        (await RunAsync(cancellationToken, "rev-parse", "HEAD")).Single();
}
