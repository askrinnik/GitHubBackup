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
    private const string _mainCommit = "1111111111111111111111111111111111111111";
    private const string _developCommit = "2222222222222222222222222222222222222222";
    private const string _tagObject = "3333333333333333333333333333333333333333";

    private static readonly string[] _fetchArguments = ["fetch", "origin", "--prune", "--prune-tags", "--tags", "--force"];

    private static readonly string[] _localRefsArguments =
        ["for-each-ref", "--format=%(objectname)%09%(refname)", "refs/remotes/origin", "refs/tags"];

    private static readonly string[] _defaultBranchArguments = ["ls-remote", "--symref", "origin", "HEAD"];

    private static readonly string[][] _syncSteps = SyncSteps("main");

    private readonly MockFileSystem _fileSystem = new();
    private readonly SecretMasker _masker = new();
    private readonly RecordingLogger<GitClient> _logger = new();
    private readonly List<ProcessRequest> _requests = [];
    private Func<ProcessRequest, ProcessResult> _respond;

    private string[] _localRefs =
    [
        $"{_mainCommit}\trefs/remotes/origin/HEAD",
        $"{_mainCommit}\trefs/remotes/origin/main",
        $"{_developCommit}\trefs/remotes/origin/develop",
        $"{_tagObject}\trefs/tags/v1",
    ];

    private string[] _defaultBranch = ["ref: refs/heads/main\tHEAD", $"{_mainCommit}\tHEAD"];

    /// <summary>
    /// Answers every git command with success; <c>for-each-ref</c> and <c>ls-remote --symref</c> print the refs and
    /// the default branch the test sets up.
    /// </summary>
    public GitClientTests() => _respond = Respond;

    /// <summary>
    /// The operations of <see cref="IGitClient"/>, for tests that hold for several of them.
    /// </summary>
    public enum Operation
    {
        /// <summary><see cref="IGitClient.CloneAsync"/>.</summary>
        Clone,

        /// <summary><see cref="IGitClient.UpdateAsync"/>.</summary>
        Update,

        /// <summary><see cref="IGitClient.GetRemoteRefsAsync"/>.</summary>
        RemoteRefs,

        /// <summary><see cref="IGitClient.GetLocalRefsAsync"/>.</summary>
        LocalRefs,

        /// <summary><see cref="IGitClient.GetDefaultBranchAsync"/>.</summary>
        DefaultBranch,
    }

    [Fact]
    public async Task CloneAsync_ValidRequest_ClonesWithoutCheckoutThenSynchronizesTheBranch()
    {
        await CreateClient().CloneAsync(Request(), _remoteUrl, TestContext.Current.CancellationToken);

        _requests.Select(request => request.Arguments).ShouldBe(
        [
            ["clone", "--no-checkout", "--origin", "origin", "--", _remoteUrl, _clonePath],
            _localRefsArguments,
            .. _syncSteps,
        ]);
        _requests.Select(request => request.WorkingDirectory)
            .ShouldBe([_parentPath, _clonePath, .. _syncSteps.Select(_ => _clonePath)]);
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

        _requests.Select(request => request.Arguments).ShouldBe([_fetchArguments, _localRefsArguments, .. _syncSteps]);
        _requests.ShouldAllBe(request => request.WorkingDirectory == _clonePath && request.FileName == _gitPath);
        _logger.Entries.ShouldBe([(LogLevel.Information, $"Updating {_clonePath}, branch main")]);
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_BranchOnServer_ChecksItOutWithoutAskingForDefault(Operation operation)
    {
        var result = await SyncAsync(operation, Request() with { Branch = "develop" });

        result.ShouldBe(new("develop", false, DefaultRefs()));
        result.IsEmpty.ShouldBeFalse();
        _requests.Select(request => request.Arguments).Skip(2).ShouldBe(SyncSteps("develop"));
        _requests.ShouldNotContain(request => request.Arguments.SequenceEqual(_defaultBranchArguments));
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_BranchNotSet_ChecksOutServerDefault(Operation operation)
    {
        _defaultBranch = ["ref: refs/heads/develop\tHEAD", $"{_developCommit}\tHEAD"];

        var result = await SyncAsync(operation, Request() with { Branch = null });

        result.ShouldBe(new("develop", false, DefaultRefs()));
        _requests.Select(request => request.Arguments).Skip(1)
            .ShouldBe([_localRefsArguments, _defaultBranchArguments, .. SyncSteps("develop")]);
        _logger.Entries.ShouldContain((LogLevel.Information, $"Checking out the default branch develop in {_clonePath}"));
        _logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_BranchMissingOnServer_FallsBackToDefaultWithWarning(Operation operation)
    {
        var result = await SyncAsync(operation, Request() with { Branch = "gone" });

        result.ShouldBe(new("main", true, DefaultRefs()));
        _requests.Select(request => request.Arguments).Skip(1)
            .ShouldBe([_localRefsArguments, _defaultBranchArguments, .. _syncSteps]);
        _logger.Entries.ShouldContain((
            LogLevel.Warning,
            $"Branch gone does not exist on the server of {_clonePath}; checking out the default branch main"));
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_NoBranchOnServer_ReturnsEmptyResultWithoutCheckout(Operation operation)
    {
        _localRefs = [];

        var result = await SyncAsync(operation, Request());

        result.ShouldBe(new(null, false, RefSnapshot.Empty));
        result.IsEmpty.ShouldBeTrue();
        _requests.Count.ShouldBe(2);
        _requests[1].Arguments.ShouldBe(_localRefsArguments);
        _logger.Entries.ShouldContain(
            (LogLevel.Information, $"The repository of {_clonePath} is empty on the server; nothing to check out"));
    }

    [Fact]
    public async Task UpdateAsync_OnlyTagsLeftOnServer_ReturnsEmptyResultWithTheTags()
    {
        _localRefs = [$"{_tagObject}\trefs/tags/v1"];

        var result = await CreateClient().UpdateAsync(Request(), TestContext.Current.CancellationToken);

        result.IsEmpty.ShouldBeTrue();
        result.Refs.ShouldBe(new RefSnapshot([new("tags/v1", _tagObject)]));
        _requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("gone")]
    public async Task UpdateAsync_DefaultBranchUnresolved_ThrowsBranchNotFoundWithoutCheckout(string? branch)
    {
        _defaultBranch = [];

        var exception = await Should.ThrowAsync<GitBranchNotFoundException>(
            () => CreateClient().UpdateAsync(Request() with { Branch = branch }, TestContext.Current.CancellationToken));

        exception.RequestedBranch.ShouldBe(branch);
        exception.DefaultBranch.ShouldBeNull();
        exception.Message.ShouldContain(_clonePath);
        _requests.ShouldNotContain(request => request.Arguments[0] == "checkout");
    }

    [Fact]
    public async Task UpdateAsync_DefaultBranchNotAmongFetchedBranches_ThrowsBranchNotFound()
    {
        _defaultBranch = ["ref: refs/heads/other\tHEAD", $"{_mainCommit}\tHEAD"];

        var exception = await Should.ThrowAsync<GitBranchNotFoundException>(
            () => CreateClient().UpdateAsync(Request() with { Branch = "gone" }, TestContext.Current.CancellationToken));

        exception.RequestedBranch.ShouldBe("gone");
        exception.DefaultBranch.ShouldBe("other");
        exception.Message.ShouldContain("'gone'");
        exception.Message.ShouldContain("'other'");
        _requests.ShouldNotContain(request => request.Arguments[0] == "checkout");
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    public async Task Operation_DefaultBranchStartsWithDash_ThrowsBranchNotFoundBeforeCheckout(Operation operation)
    {
        _localRefs = [$"{_mainCommit}\trefs/remotes/origin/--upload-pack=evil"];
        _defaultBranch = ["ref: refs/heads/--upload-pack=evil\tHEAD", $"{_mainCommit}\tHEAD"];

        var exception = await Should.ThrowAsync<GitBranchNotFoundException>(
            () => SyncAsync(operation, Request() with { Branch = null }));

        exception.DefaultBranch.ShouldBeNull();
        _requests.ShouldNotContain(request => request.Arguments[0] == "checkout");
    }

    [Fact]
    public async Task GetDefaultBranchAsync_ServerNamesInvalidBranch_ReturnsNull()
    {
        _defaultBranch = ["ref: refs/heads/-b\tHEAD", $"{_mainCommit}\tHEAD"];

        var branch = await CreateClient().GetDefaultBranchAsync(Request(), TestContext.Current.CancellationToken);

        branch.ShouldBeNull();
    }

    [Theory]
    [InlineData(Operation.RemoteRefs, "ls-remote")]
    [InlineData(Operation.LocalRefs, "for-each-ref")]
    public async Task Operation_MalformedRefLine_ThrowsGitCommandExceptionNamingClonePath(
        Operation operation, string command)
    {
        _respond = _ => Output("not a ref line");

        var exception = await Should.ThrowAsync<GitCommandException>(() => RunAsync(operation, Request()));

        exception.Message.ShouldContain(_clonePath);
        exception.Command.ShouldStartWith("git " + command);
    }

    [Theory]
    [InlineData(Operation.RemoteRefs, "refs/heads/")]
    [InlineData(Operation.RemoteRefs, "refs/tags/")]
    [InlineData(Operation.LocalRefs, "refs/tags/")]
    [InlineData(Operation.LocalRefs, "refs/remotes/origin/")]
    public async Task Operation_EmptyRefName_ThrowsGitCommandException(Operation operation, string name)
    {
        _respond = _ => Output($"{_mainCommit}\t{name}");

        var exception = await Should.ThrowAsync<GitCommandException>(() => RunAsync(operation, Request()));

        exception.Message.ShouldContain(_clonePath);
    }

    [Theory]
    [InlineData(Operation.RemoteRefs, "refs/heads/main")]
    [InlineData(Operation.LocalRefs, "refs/tags/v1")]
    public async Task Operation_DuplicateRef_ThrowsGitCommandException(Operation operation, string name)
    {
        _respond = _ => Output($"{_mainCommit}\t{name}", $"{_developCommit}\t{name}");

        var exception = await Should.ThrowAsync<GitCommandException>(() => RunAsync(operation, Request()));

        exception.Message.ShouldContain(_clonePath);
    }

    [Theory]
    [InlineData(_clonePath + @"\")]
    [InlineData(_clonePath + @"\.")]
    [InlineData(_clonePath + @"\sub\..")]
    public async Task UpdateAsync_ClonePathNotNormalized_SetsCeilingToRealParent(string clonePath)
    {
        await CreateClient().UpdateAsync(Request() with { ClonePath = clonePath }, TestContext.Current.CancellationToken);

        _requests.ShouldNotBeEmpty();
        _requests.ShouldAllBe(request => Variables(request)["GIT_CEILING_DIRECTORIES"] == _parentPath);
    }

    [Fact]
    public async Task UpdateAsync_ParentPathContainsSemicolon_SetsNoCeiling()
    {
        await CreateClient().UpdateAsync(
            Request() with { ClonePath = @"C:\backup\a;b\repo" }, TestContext.Current.CancellationToken);

        _requests.ShouldNotBeEmpty();
        _requests.ShouldAllBe(request => !Variables(request).ContainsKey("GIT_CEILING_DIRECTORIES"));
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    [InlineData(Operation.RemoteRefs)]
    [InlineData(Operation.LocalRefs)]
    [InlineData(Operation.DefaultBranch)]
    public async Task Operation_AnyStep_SetsCeilingDirectoryToParentOfClone(Operation operation)
    {
        await RunAsync(operation, Request(_token) with { Branch = null });

        _requests.ShouldNotBeEmpty();
        _requests.ShouldAllBe(request => Variables(request)["GIT_CEILING_DIRECTORIES"] == _parentPath);
    }

    [Fact]
    public async Task GetRemoteRefsAsync_ValidRequest_ListsServerBranchesAndTagsInTheClone()
    {
        _respond = request => Output(
            $"{_mainCommit}\trefs/heads/main",
            $"{_developCommit}\trefs/pull/1/head",
            $"{_tagObject}\trefs/tags/v1");

        var refs = await CreateClient().GetRemoteRefsAsync(Request(), TestContext.Current.CancellationToken);

        refs.ShouldBe(new RefSnapshot([new("heads/main", _mainCommit), new("tags/v1", _tagObject)]));
        _requests.Single().Arguments.ShouldBe(["ls-remote", "--refs", "origin"]);
        _requests.Single().WorkingDirectory.ShouldBe(_clonePath);
    }

    [Fact]
    public async Task GetLocalRefsAsync_ValidRequest_ListsTrackingBranchesAndTagsOfTheClone()
    {
        var refs = await CreateClient().GetLocalRefsAsync(Request(), TestContext.Current.CancellationToken);

        refs.ShouldBe(DefaultRefs());
        _requests.Single().Arguments.ShouldBe(_localRefsArguments);
        _requests.Single().WorkingDirectory.ShouldBe(_clonePath);
    }

    [Fact]
    public async Task GetDefaultBranchAsync_ValidRequest_AsksServerForHead()
    {
        var branch = await CreateClient().GetDefaultBranchAsync(Request(), TestContext.Current.CancellationToken);

        branch.ShouldBe("main");
        _requests.Single().Arguments.ShouldBe(_defaultBranchArguments);
        _requests.Single().WorkingDirectory.ShouldBe(_clonePath);
    }

    [Fact]
    public async Task GetDefaultBranchAsync_ServerHasNoBranch_ReturnsNull()
    {
        _defaultBranch = [];

        var branch = await CreateClient().GetDefaultBranchAsync(Request(), TestContext.Current.CancellationToken);

        branch.ShouldBeNull();
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    [InlineData(Operation.RemoteRefs)]
    [InlineData(Operation.LocalRefs)]
    [InlineData(Operation.DefaultBranch)]
    public async Task Operation_AnyStep_DisablesPromptsAndEnablesLongPaths(Operation operation)
    {
        await RunAsync(operation, Request(_token));

        _requests.ShouldNotBeEmpty();
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
    [InlineData(Operation.RemoteRefs)]
    [InlineData(Operation.LocalRefs)]
    [InlineData(Operation.DefaultBranch)]
    public async Task Operation_AnyStep_ClearsCredentialHelpers(Operation operation)
    {
        await RunAsync(operation, Request(_token));

        foreach (var request in _requests)
        {
            var variables = Variables(request);
            variables["GIT_CONFIG_KEY_1"].ShouldBe("credential.helper");
            variables["GIT_CONFIG_VALUE_1"].ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    [InlineData(Operation.RemoteRefs)]
    [InlineData(Operation.LocalRefs)]
    [InlineData(Operation.DefaultBranch)]
    public async Task Operation_AnyStep_DisablesSymbolicLinks(Operation operation)
    {
        await RunAsync(operation, Request(_token));

        _requests.ShouldNotBeEmpty();
        foreach (var request in _requests)
        {
            var variables = Variables(request);
            variables["GIT_CONFIG_KEY_2"].ShouldBe("core.symlinks");
            variables["GIT_CONFIG_VALUE_2"].ShouldBe("false");
        }
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    [InlineData(Operation.RemoteRefs)]
    [InlineData(Operation.DefaultBranch)]
    public async Task Operation_TokenSet_PassesBasicExtraHeaderAsSecretOnEveryStep(Operation operation)
    {
        var expectedHeader = "Authorization: Basic "
            + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + _token));

        await RunAsync(operation, Request(_token) with { Branch = null });

        _requests.ShouldContain(request => request.Arguments[0] == "ls-remote");
        foreach (var request in _requests)
        {
            var variables = Variables(request);
            variables["GIT_CONFIG_COUNT"].ShouldBe("4");
            variables["GIT_CONFIG_KEY_3"].ShouldBe("http.https://github.com/.extraHeader");
            var header = request.EnvironmentVariables.Single(variable => variable.Name == "GIT_CONFIG_VALUE_3");
            header.Value.ShouldBe(expectedHeader);
            header.IsSecret.ShouldBeTrue();
            request.EnvironmentVariables.Where(variable => variable.Name != "GIT_CONFIG_VALUE_3")
                .ShouldAllBe(variable => !variable.IsSecret);
        }
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    [InlineData(Operation.RemoteRefs)]
    [InlineData(Operation.DefaultBranch)]
    public async Task Operation_TokenSet_KeepsTokenOutOfArguments(Operation operation)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + _token));

        await RunAsync(operation, Request(_token) with { Branch = null });

        _requests.SelectMany(request => request.Arguments)
            .ShouldAllBe(argument => !argument.Contains(_token) && !argument.Contains(encoded));
    }

    [Fact]
    public async Task GetLocalRefsAsync_TokenSet_PassesNoExtraHeader()
    {
        await CreateClient().GetLocalRefsAsync(Request(_token), TestContext.Current.CancellationToken);

        Variables(_requests.Single())["GIT_CONFIG_COUNT"].ShouldBe("3");
        _requests.Single().EnvironmentVariables.ShouldAllBe(variable => !variable.IsSecret);
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
            Variables(request)["GIT_CONFIG_COUNT"].ShouldBe("3");
            request.EnvironmentVariables.ShouldAllBe(variable => !variable.Value.Contains("extraHeader") && !variable.IsSecret);
        }
    }

    [Fact]
    public async Task UpdateAsync_AdditionalConfiguration_PlacesItBetweenFixedEntriesAndHeader()
    {
        var client = new GitClient(CreateRunner(), _fileSystem, _masker, _logger)
        {
            AdditionalConfiguration = [new("protocol.file.allow", "always")],
        };

        await client.UpdateAsync(Request(_token), TestContext.Current.CancellationToken);

        var variables = Variables(_requests[0]);
        variables["GIT_CONFIG_COUNT"].ShouldBe("5");
        variables["GIT_CONFIG_KEY_0"].ShouldBe("core.longpaths");
        variables["GIT_CONFIG_KEY_1"].ShouldBe("credential.helper");
        variables["GIT_CONFIG_KEY_2"].ShouldBe("core.symlinks");
        variables["GIT_CONFIG_KEY_3"].ShouldBe("protocol.file.allow");
        variables["GIT_CONFIG_VALUE_3"].ShouldBe("always");
        variables["GIT_CONFIG_KEY_4"].ShouldBe("http.https://github.com/.extraHeader");
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    [InlineData(Operation.RemoteRefs)]
    [InlineData(Operation.DefaultBranch)]
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
            : Respond(request);

        var exception = await Should.ThrowAsync<GitCommandException>(
            () => CreateClient().UpdateAsync(Request(_token), TestContext.Current.CancellationToken));

        exception.ExitCode.ShouldBe(128);
        exception.Command.ShouldBe("git reset --hard origin/main --");
        exception.Message.ShouldContain("git reset --hard origin/main --");
        exception.Message.ShouldContain(_clonePath);
        exception.Message.ShouldContain("128");
        exception.Message.ShouldContain("fatal: could not read with ***");
        exception.Message.ShouldNotContain(_token);
        _requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetRemoteRefsAsync_LsRemoteFails_ThrowsWithMaskedError()
    {
        _respond = _ => Result(128, $"fatal: Authentication failed for {_token}");

        var exception = await Should.ThrowAsync<GitCommandException>(
            () => CreateClient().GetRemoteRefsAsync(Request(_token), TestContext.Current.CancellationToken));

        exception.Command.ShouldBe("git ls-remote --refs origin");
        exception.Message.ShouldNotContain(_token);
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

            return Respond(request);
        };

        await Should.ThrowAsync<OperationCanceledException>(() => CreateClient().UpdateAsync(Request(), source.Token));

        _requests.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    [InlineData(Operation.RemoteRefs)]
    [InlineData(Operation.LocalRefs)]
    [InlineData(Operation.DefaultBranch)]
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
    [InlineData(Operation.RemoteRefs)]
    [InlineData(Operation.LocalRefs)]
    [InlineData(Operation.DefaultBranch)]
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
    /// Returns a successful process result with the given standard output lines.
    /// </summary>
    /// <param name="standardOutput">The lines on standard output.</param>
    /// <returns>The result.</returns>
    private static ProcessResult Output(params string[] standardOutput) =>
        new(0, TimeSpan.FromSeconds(1), standardOutput, []);

    /// <summary>
    /// Returns the arguments of the steps that bring the working tree to <paramref name="branch"/>.
    /// </summary>
    /// <param name="branch">The branch checked out.</param>
    /// <returns>The arguments of each step.</returns>
    private static string[][] SyncSteps(string branch) =>
    [
        ["checkout", "-B", branch, "origin/" + branch, "--"],
        ["reset", "--hard", "origin/" + branch, "--"],
        ["clean", "-ffdx"],
        ["submodule", "sync", "--recursive"],
        ["submodule", "update", "--init", "--recursive", "--force"],
        ["lfs", "pull"],
    ];

    /// <summary>
    /// Returns the snapshot that the default output of <c>for-each-ref</c> describes.
    /// </summary>
    /// <returns>The snapshot.</returns>
    private static RefSnapshot DefaultRefs() => new(
    [
        new("heads/main", _mainCommit),
        new("heads/develop", _developCommit),
        new("tags/v1", _tagObject),
    ]);

    /// <summary>
    /// Returns the environment variables of <paramref name="request"/> by name.
    /// </summary>
    /// <param name="request">The recorded request.</param>
    /// <returns>The value per variable name.</returns>
    private static Dictionary<string, string> Variables(ProcessRequest request) =>
        request.EnvironmentVariables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal);

    /// <summary>
    /// Answers a git command with success and, for the ref queries, with the output the test set up.
    /// </summary>
    /// <param name="request">The recorded request.</param>
    /// <returns>The result.</returns>
    private ProcessResult Respond(ProcessRequest request) => request.Arguments switch
    {
        ["for-each-ref", ..] => Output(_localRefs),
        ["ls-remote", "--symref", ..] => Output(_defaultBranch),
        _ => Result(0),
    };

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
        return operation switch
        {
            Operation.Clone => client.CloneAsync(request, _remoteUrl, token),
            Operation.Update => client.UpdateAsync(request, token),
            Operation.RemoteRefs => client.GetRemoteRefsAsync(request, token),
            Operation.LocalRefs => client.GetLocalRefsAsync(request, token),
            _ => client.GetDefaultBranchAsync(request, token),
        };
    }

    /// <summary>
    /// Runs <see cref="IGitClient.CloneAsync"/> or <see cref="IGitClient.UpdateAsync"/> with a client over the fakes.
    /// </summary>
    /// <param name="operation"><see cref="Operation.Clone"/> or <see cref="Operation.Update"/>.</param>
    /// <param name="request">The request.</param>
    /// <returns>The result of the operation.</returns>
    private Task<GitSyncResult> SyncAsync(Operation operation, GitRepositoryRequest request)
    {
        var client = CreateClient();
        return operation == Operation.Clone
            ? client.CloneAsync(request, _remoteUrl, TestContext.Current.CancellationToken)
            : client.UpdateAsync(request, TestContext.Current.CancellationToken);
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
