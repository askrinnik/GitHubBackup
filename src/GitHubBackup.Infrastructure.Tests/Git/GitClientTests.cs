using System.IO.Abstractions.TestingHelpers;
using System.Text;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.Tests.BackupConfiguration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Git;

/// <summary>
/// Tests <see cref="GitClient"/> with a fake process runner, a fake file system and the real secret masker.
/// </summary>
public sealed class GitClientTests
{
    private const string _gitPath = @"C:\Git\cmd\git.exe";
    private const string _parentPath = @"C:\backup\sources\owner";
    private const string _clonePath = @"C:\backup\sources\owner\repo";
    private const string _remoteUrl = "https://github.com/owner/repo.git";
    private const string _token = "ghp_0123456789abcdefghijklmnopqrstuvwxyzAB";

    private static readonly string[][] _syncSteps =
    [
        ["checkout", "-B", "main", "origin/main"],
        ["reset", "--hard", "origin/main"],
        ["clean", "-ffdx"],
        ["submodule", "sync", "--recursive"],
        ["submodule", "update", "--init", "--recursive", "--force"],
        ["lfs", "pull"],
    ];

    private readonly MockFileSystem _fileSystem = new();
    private readonly SecretMasker _masker = new();
    private readonly RecordingLogger<GitClient> _logger = new();
    private readonly List<ProcessRequest> _requests = [];
    private Func<ProcessRequest, ProcessResult> _respond = _ => Result(0);

    /// <summary>
    /// The operations of <see cref="IGitClient"/>, for tests that hold for both.
    /// </summary>
    public enum Operation
    {
        /// <summary><see cref="IGitClient.CloneAsync"/>.</summary>
        Clone,

        /// <summary><see cref="IGitClient.UpdateAsync"/>.</summary>
        Update,
    }

    [Fact]
    public async Task CloneAsync_ValidRequest_ClonesWithoutCheckoutThenSynchronizesTheBranch()
    {
        await CreateClient().CloneAsync(Request(), _remoteUrl, TestContext.Current.CancellationToken);

        _requests.Select(request => request.Arguments).ShouldBe(
        [
            ["clone", "--no-checkout", "--origin", "origin", "--", _remoteUrl, _clonePath],
            .. _syncSteps,
        ]);
        _requests.Select(request => request.WorkingDirectory).ShouldBe([_parentPath, .. _syncSteps.Select(_ => _clonePath)]);
        _requests.ShouldAllBe(request => request.FileName == _gitPath);
    }

    [Fact]
    public async Task CloneAsync_ParentFolderMissing_CreatesIt()
    {
        await CreateClient().CloneAsync(Request(), _remoteUrl, TestContext.Current.CancellationToken);

        _fileSystem.Directory.Exists(_parentPath).ShouldBeTrue();
        _fileSystem.Directory.Exists(_clonePath).ShouldBeFalse();
    }

    [Fact]
    public async Task CloneAsync_ValidRequest_LogsUrlPathAndBranch()
    {
        await CreateClient().CloneAsync(Request(), _remoteUrl, TestContext.Current.CancellationToken);

        _logger.Entries.ShouldBe([(LogLevel.Information, $"Cloning {_remoteUrl} into {_clonePath}, branch main")]);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_FetchesWithPruningThenSynchronizesTheBranch()
    {
        await CreateClient().UpdateAsync(Request(), TestContext.Current.CancellationToken);

        _requests.Select(request => request.Arguments).ShouldBe(
        [
            ["fetch", "origin", "--prune", "--prune-tags", "--tags", "--force"],
            .. _syncSteps,
        ]);
        _requests.ShouldAllBe(request => request.WorkingDirectory == _clonePath && request.FileName == _gitPath);
        _logger.Entries.ShouldBe([(LogLevel.Information, $"Updating {_clonePath}, branch main")]);
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_AnyStep_DisablesPromptsAndEnablesLongPaths(Operation operation)
    {
        await RunAsync(operation, Request(_token));

        _requests.Count.ShouldBe(7);
        foreach (var request in _requests)
        {
            var variables = Variables(request);
            variables["GIT_TERMINAL_PROMPT"].ShouldBe("0");
            variables["GCM_INTERACTIVE"].ShouldBe("never");
            variables["GIT_CONFIG_KEY_0"].ShouldBe("core.longpaths");
            variables["GIT_CONFIG_VALUE_0"].ShouldBe("true");
        }
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_TokenSet_PassesBasicExtraHeaderAsSecretOnEveryStep(Operation operation)
    {
        var expectedHeader = "Authorization: Basic "
            + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + _token));

        await RunAsync(operation, Request(_token));

        foreach (var request in _requests)
        {
            var variables = Variables(request);
            variables["GIT_CONFIG_COUNT"].ShouldBe("2");
            variables["GIT_CONFIG_KEY_1"].ShouldBe("http.https://github.com/.extraHeader");
            var header = request.EnvironmentVariables.Single(variable => variable.Name == "GIT_CONFIG_VALUE_1");
            header.Value.ShouldBe(expectedHeader);
            header.IsSecret.ShouldBeTrue();
            request.EnvironmentVariables.Where(variable => variable.Name != "GIT_CONFIG_VALUE_1")
                .ShouldAllBe(variable => !variable.IsSecret);
        }
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_TokenSet_KeepsTokenOutOfArguments(Operation operation)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + _token));

        await RunAsync(operation, Request(_token));

        _requests.SelectMany(request => request.Arguments)
            .ShouldAllBe(argument => !argument.Contains(_token) && !argument.Contains(encoded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CloneAsync_NoToken_PassesNoExtraHeader(string? token)
    {
        await CreateClient().CloneAsync(Request(token), _remoteUrl, TestContext.Current.CancellationToken);

        foreach (var request in _requests)
        {
            Variables(request)["GIT_CONFIG_COUNT"].ShouldBe("1");
            request.EnvironmentVariables.ShouldAllBe(variable => !variable.Value.Contains("extraHeader") && !variable.IsSecret);
        }
    }

    [Fact]
    public async Task UpdateAsync_AdditionalConfiguration_PlacesItBetweenLongPathsAndHeader()
    {
        var client = new GitClient(CreateRunner(), _fileSystem, _masker, _logger)
        {
            AdditionalConfiguration = [new("protocol.file.allow", "always")],
        };

        await client.UpdateAsync(Request(_token), TestContext.Current.CancellationToken);

        var variables = Variables(_requests[0]);
        variables["GIT_CONFIG_COUNT"].ShouldBe("3");
        variables["GIT_CONFIG_KEY_0"].ShouldBe("core.longpaths");
        variables["GIT_CONFIG_KEY_1"].ShouldBe("protocol.file.allow");
        variables["GIT_CONFIG_VALUE_1"].ShouldBe("always");
        variables["GIT_CONFIG_KEY_2"].ShouldBe("http.https://github.com/.extraHeader");
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_TokenSet_RegistersTokenAndEncodedCredentialsWithMasker(Operation operation)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + _token));

        await RunAsync(operation, Request(_token));

        _masker.Mask($"token {_token} and {encoded}").ShouldBe("token *** and ***");
    }

    [Fact]
    public async Task UpdateAsync_StepFails_ThrowsWithMaskedErrorAndStops()
    {
        _respond = request => request.Arguments[0] == "reset"
            ? Result(128, "warning: progress", $"fatal: could not read with {_token}")
            : Result(0);

        var exception = await Should.ThrowAsync<GitCommandException>(
            () => CreateClient().UpdateAsync(Request(_token), TestContext.Current.CancellationToken));

        exception.ExitCode.ShouldBe(128);
        exception.Command.ShouldBe("git reset --hard origin/main");
        exception.Message.ShouldContain("git reset --hard origin/main");
        exception.Message.ShouldContain(_clonePath);
        exception.Message.ShouldContain("128");
        exception.Message.ShouldContain("fatal: could not read with ***");
        exception.Message.ShouldNotContain(_token);
        _requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task CloneAsync_CloneFails_ThrowsAndRunsNoFurtherStep()
    {
        _respond = _ => Result(128, "fatal: repository not found");

        var exception = await Should.ThrowAsync<GitCommandException>(
            () => CreateClient().CloneAsync(Request(), _remoteUrl, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("fatal: repository not found");
        _requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateAsync_StepCancelled_PropagatesCancellationAndStops()
    {
        using var source = new CancellationTokenSource();
        _respond = request =>
        {
            if (request.Arguments[0] == "checkout")
            {
                source.Cancel();
                throw new OperationCanceledException(source.Token);
            }

            return Result(0);
        };

        await Should.ThrowAsync<OperationCanceledException>(() => CreateClient().UpdateAsync(Request(), source.Token));

        _requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_AlreadyCancelled_RunsNothing(Operation operation)
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => RunAsync(operation, Request(), source.Token));

        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_GitCannotStart_PropagatesProcessStartException()
    {
        var failure = new ProcessStartException("The executable does not exist.");
        _respond = _ => throw failure;

        var exception = await Should.ThrowAsync<ProcessStartException>(
            () => CreateClient().UpdateAsync(Request(), TestContext.Current.CancellationToken));

        exception.ShouldBeSameAs(failure);
        _requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(Operation.Clone, "")]
    [InlineData(Operation.Clone, "   ")]
    [InlineData(Operation.Clone, "-b")]
    [InlineData(Operation.Clone, "--upload-pack=evil")]
    [InlineData(Operation.Update, "")]
    [InlineData(Operation.Update, "--upload-pack=evil")]
    public async Task Operation_InvalidBranch_ThrowsBeforeRunningGit(Operation operation, string branch)
    {
        await Should.ThrowAsync<ArgumentException>(() => RunAsync(operation, Request() with { Branch = branch }));

        _requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_RelativeClonePath_ThrowsBeforeRunningGit(Operation operation)
    {
        await Should.ThrowAsync<ArgumentException>(() => RunAsync(operation, Request() with { ClonePath = @"owner\repo" }));

        _requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CloneAsync_EmptyRemoteUrl_ThrowsBeforeRunningGit(string remoteUrl)
    {
        await Should.ThrowAsync<ArgumentException>(
            () => CreateClient().CloneAsync(Request(), remoteUrl, TestContext.Current.CancellationToken));

        _requests.ShouldBeEmpty();
        _fileSystem.Directory.Exists(_parentPath).ShouldBeFalse();
    }

    /// <summary>
    /// Returns a request for <c>main</c> in the test clone folder.
    /// </summary>
    /// <param name="token">The token, or <see langword="null"/> for anonymous access.</param>
    /// <returns>The request.</returns>
    private static GitRepositoryRequest Request(string? token = null) => new()
    {
        GitPath = _gitPath,
        ClonePath = _clonePath,
        Branch = "main",
        Token = token,
    };

    /// <summary>
    /// Returns a process result with the given exit code and standard error lines.
    /// </summary>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="standardError">The lines on standard error.</param>
    /// <returns>The result.</returns>
    private static ProcessResult Result(int exitCode, params string[] standardError) =>
        new(exitCode, TimeSpan.FromSeconds(1), [], standardError);

    /// <summary>
    /// Returns the environment variables of <paramref name="request"/> by name.
    /// </summary>
    /// <param name="request">The recorded request.</param>
    /// <returns>The value per variable name.</returns>
    private static Dictionary<string, string> Variables(ProcessRequest request) =>
        request.EnvironmentVariables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal);

    /// <summary>
    /// Runs <paramref name="operation"/> with a client over the fakes.
    /// </summary>
    /// <param name="operation">The operation to run.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">
    /// The token, or <see langword="null"/> for the token of the test.
    /// </param>
    /// <returns>A task that completes when the operation does.</returns>
    private Task RunAsync(Operation operation, GitRepositoryRequest request, CancellationToken? cancellationToken = null)
    {
        var token = cancellationToken ?? TestContext.Current.CancellationToken;
        var client = CreateClient();
        return operation == Operation.Clone
            ? client.CloneAsync(request, _remoteUrl, token)
            : client.UpdateAsync(request, token);
    }

    /// <summary>
    /// Creates the client under test over the fakes.
    /// </summary>
    /// <returns>The client.</returns>
    private GitClient CreateClient() => new(CreateRunner(), _fileSystem, _masker, _logger);

    /// <summary>
    /// Creates a process runner that records every request and answers with <see cref="_respond"/>.
    /// </summary>
    /// <returns>The fake runner.</returns>
    private IProcessRunner CreateRunner()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<ProcessRequest>();
            _requests.Add(request);
            return Task.FromResult(_respond(request));
        });
        return runner;
    }
}
