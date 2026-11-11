using System.IO.Abstractions;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.TestUtilities.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Provides the per-test setup of the <see cref="GitClient"/> tests: a sandbox with local bare repositories, the clone
/// folder, capturing loggers and helpers that inspect a clone with git.
/// </summary>
/// <remarks>
/// xUnit runs the tests of one class one after another and different classes in parallel, so the
/// <see cref="GitClient"/> tests are split by topic into classes that share this base.
/// </remarks>
public abstract class GitClientTestBase : IDisposable
{
    /// <summary>
    /// The event id of the entry <see cref="ProcessRunner"/> logs with the command line before it starts a process.
    /// </summary>
    private protected const int _processStartingEventId = 1;

    /// <summary>
    /// The event id of the warning <see cref="GitClient"/> logs when it checks out the default branch instead of a
    /// missing one.
    /// </summary>
    private protected const int _branchNotFoundEventId = 3;

    /// <summary>
    /// The event id of the entry <see cref="GitClient"/> logs for a repository without branches.
    /// </summary>
    private protected const int _emptyRepositoryEventId = 4;

    /// <summary>
    /// The event id of the entry <see cref="GitClient"/> logs before it changes the <c>origin</c> URL.
    /// </summary>
    private protected const int _settingRemoteUrlEventId = 6;
    /// <summary>
    /// The sandbox that owns the temporary folder, the remotes and the isolated git configuration.
    /// </summary>
    private protected readonly LocalGitSandbox _sandbox = new();

    /// <summary>
    /// The logger of the process runner the client starts git through.
    /// </summary>
    private protected readonly FakeLogger<ProcessRunner> _runnerLogger = new();

    /// <summary>
    /// The logger of the client under test.
    /// </summary>
    private protected readonly FakeLogger<GitClient> _clientLogger = new();

    /// <summary>
    /// The clone folder of the test; its parent does not exist until the client creates it.
    /// </summary>
    private protected readonly string _clonePath;

    private readonly SecretMasker _masker = new();

    /// <summary>
    /// Creates the sandbox and chooses the clone folder.
    /// </summary>
    protected GitClientTestBase() => _clonePath = Path.Combine(_sandbox.RootPath, "backup", "owner", "repo");

    /// <summary>
    /// Gets the cancellation token of the running test.
    /// </summary>
    private protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Deletes the sandbox.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Deletes the sandbox when called from <see cref="Dispose()"/>.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sandbox.Dispose();
        }
    }

    /// <summary>
    /// Creates a client over the real file system and the real process runner, allowing submodules from
    /// <c>file://</c> remotes.
    /// </summary>
    /// <returns>The client under test.</returns>
    private protected GitClient CreateClient()
    {
        var fileSystem = new FileSystem();
        return new(new ProcessRunner(_masker, fileSystem, TimeProvider.System, _runnerLogger), fileSystem, _masker, _clientLogger)
        {
            AdditionalConfiguration = [new("protocol.file.allow", "always")],
        };
    }

    /// <summary>
    /// Returns a request for the clone folder of the test.
    /// </summary>
    /// <param name="branch">The branch to check out, or <see langword="null"/> for the server's default branch.</param>
    /// <param name="token">The token, or <see langword="null"/> for anonymous access.</param>
    /// <returns>The request.</returns>
    private protected GitRepositoryRequest Request(string? branch = "main", string? token = null) => new()
    {
        GitPath = _sandbox.GitPath,
        ClonePath = _clonePath,
        Branch = branch,
        Token = token,
    };

    /// <summary>
    /// Runs git in the isolated environment of the sandbox to inspect a clone.
    /// </summary>
    /// <param name="workingDirectory">The folder git runs in.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The non-empty lines of standard output.</returns>
    private protected Task<IReadOnlyList<string>> GitAsync(string workingDirectory, params string[] arguments) =>
        _sandbox.RunGitAsync(workingDirectory, CancellationToken, arguments);

    /// <summary>
    /// Returns the commit checked out in <paramref name="workingDirectory"/>.
    /// </summary>
    /// <param name="workingDirectory">The clone or submodule folder.</param>
    /// <returns>The commit id.</returns>
    private protected async Task<string> HeadAsync(string workingDirectory) =>
        (await GitAsync(workingDirectory, "rev-parse", "HEAD")).Single();

    /// <summary>
    /// Returns the names of the remote-tracking branches and tags of a clone.
    /// </summary>
    /// <param name="workingDirectory">The clone folder.</param>
    /// <returns>The full ref names.</returns>
    private protected Task<IReadOnlyList<string>> RefsAsync(string workingDirectory) =>
        GitAsync(workingDirectory, "for-each-ref", "--format=%(refname)", "refs/remotes", "refs/tags");

    /// <summary>
    /// Returns the records of <paramref name="logger"/> with the event id <paramref name="eventId"/>, in the order
    /// they were written.
    /// </summary>
    /// <typeparam name="T">The category type of the logger.</typeparam>
    /// <param name="logger">The logger.</param>
    /// <param name="eventId">The event id of the <see cref="LoggerMessageAttribute"/>.</param>
    /// <returns>The records.</returns>
    private protected static List<FakeLogRecord> Records<T>(FakeLogger<T> logger, int eventId) =>
        [.. logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
