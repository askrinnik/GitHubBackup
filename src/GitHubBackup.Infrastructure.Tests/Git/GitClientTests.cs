using System.IO.Abstractions.TestingHelpers;
using System.Text;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
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

    private const int _cloningEventId = 1;
    private const int _updatingEventId = 2;
    private const int _branchNotFoundEventId = 3;
    private const int _emptyRepositoryEventId = 4;
    private const int _usingDefaultBranchEventId = 5;

    private static readonly string[] _fetchArguments = ["fetch", "origin", "--prune", "--prune-tags", "--tags", "--force"];

    private static readonly string[] _localRefsArguments =
        ["for-each-ref", "--format=%(objectname)%09%(refname)", "refs/remotes/origin", "refs/tags"];

    private static readonly string[] _defaultBranchArguments = ["ls-remote", "--symref", "origin", "HEAD"];

    private static readonly string[][] _syncSteps = SyncSteps("main");

    private readonly MockFileSystem _fileSystem = new();
    private readonly SecretMasker _masker = new();
    private readonly FakeLogger<GitClient> _logger = new();
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

        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_cloningEventId);
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Url").ShouldBe(_remoteUrl);
        record.GetStructuredStateValue("Path").ShouldBe(_clonePath);
        record.GetStructuredStateValue("Branch").ShouldBe("main");
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_FetchesWithPruningThenSynchronizesTheBranch()
    {
        await CreateClient().UpdateAsync(Request(), TestContext.Current.CancellationToken);

        _requests.Select(request => request.Arguments).ShouldBe([_fetchArguments, _localRefsArguments, .. _syncSteps]);
        _requests.ShouldAllBe(request => request.WorkingDirectory == _clonePath && request.FileName == _gitPath);
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_updatingEventId);
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Path").ShouldBe(_clonePath);
        record.GetStructuredStateValue("Branch").ShouldBe("main");
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
        var record = SingleRecord(_usingDefaultBranchEventId);
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Path").ShouldBe(_clonePath);
        record.GetStructuredStateValue("Branch").ShouldBe("develop");
        _logger.Collector.GetSnapshot().ShouldNotContain(entry => entry.Level == LogLevel.Warning);
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
        var record = SingleRecord(_branchNotFoundEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Branch").ShouldBe("gone");
        record.GetStructuredStateValue("Path").ShouldBe(_clonePath);
        record.GetStructuredStateValue("DefaultBranch").ShouldBe("main");
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
        var record = SingleRecord(_emptyRepositoryEventId);
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Path").ShouldBe(_clonePath);
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
    public async Task Operation_TokenSet_PassesHeaderOnlyToStepsThatTalkToServer(Operation operation)
    {
        var expectedHeader = "Authorization: Basic "
            + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + _token));

        await RunAsync(operation, Request(_token) with { Branch = null });

        _requests.ShouldContain(request => request.Arguments[0] == "ls-remote");
        foreach (var request in _requests)
        {
            var variables = Variables(request);
            if (IsNetworkStep(request.Arguments))
            {
                variables["GIT_CONFIG_COUNT"].ShouldBe("7");
                variables["GIT_CONFIG_KEY_6"].ShouldBe("http.https://github.com/.extraHeader");
                var header = request.EnvironmentVariables.Single(variable => variable.Name == "GIT_CONFIG_VALUE_6");
                header.Value.ShouldBe(expectedHeader);
                header.IsSecret.ShouldBeTrue();
                request.EnvironmentVariables.Where(variable => variable.Name != "GIT_CONFIG_VALUE_6")
                    .ShouldAllBe(variable => !variable.IsSecret);
            }
            else
            {
                variables["GIT_CONFIG_COUNT"].ShouldBe("6", string.Join(' ', request.Arguments));
                request.EnvironmentVariables.ShouldAllBe(variable => !variable.IsSecret);
            }
        }
    }

    [Fact]
    public async Task UpdateAsync_TokenSet_KeepsHeaderAwayFromCheckoutResetAndClean()
    {
        await CreateClient().UpdateAsync(Request(_token), TestContext.Current.CancellationToken);

        _requests.Where(request => request.Arguments[0] is "checkout" or "reset" or "clean" or "for-each-ref")
            .ShouldAllBe(request => request.EnvironmentVariables.All(variable => !variable.IsSecret));
        _requests.Where(request => request.Arguments[0] is "fetch" or "lfs"
                || (request.Arguments[0] == "submodule" && request.Arguments[1] == "update"))
            .ShouldAllBe(request => request.EnvironmentVariables.Any(variable => variable.IsSecret));
    }

    [Fact]
    public async Task UpdateAsync_TokenSet_LocalStepsSkipLfsSmudgeAndLfsPullDoesNot()
    {
        await CreateClient().UpdateAsync(Request(_token), TestContext.Current.CancellationToken);

        foreach (var request in _requests.Where(request => request.Arguments[0] is "checkout" or "reset" or "clean"))
        {
            Variables(request)["GIT_LFS_SKIP_SMUDGE"].ShouldBe("1");
            request.EnvironmentVariables.ShouldAllBe(variable => !variable.IsSecret);
        }

        _requests.Single(request => request.Arguments[0] == "lfs").EnvironmentVariables
            .ShouldNotContain(variable => variable.Name == "GIT_LFS_SKIP_SMUDGE");
    }

    [Fact]
    public async Task SyncAsync_RequiredBranch_ChecksItOutWithoutAskingForDefault()
    {
        _localRefs = [$"{_mainCommit}\trefs/remotes/origin/main", $"{_developCommit}\trefs/remotes/origin/develop"];

        var result = await CreateClient().SyncAsync(Request(), "develop", TestContext.Current.CancellationToken);

        result.Branch.ShouldBe("develop");
        result.BranchFallback.ShouldBeTrue();
        _requests.ShouldNotContain(request => request.Arguments[0] == "ls-remote");
        _requests.ShouldContain(request => request.Arguments.SequenceEqual(SyncSteps("develop")[0]));
    }

    [Fact]
    public async Task SyncAsync_RequiredBranchGoneAfterFetch_ThrowsWithoutCheckout()
    {
        _localRefs = [$"{_mainCommit}\trefs/remotes/origin/main"];

        await Should.ThrowAsync<GitBranchNotFoundException>(
            () => CreateClient().SyncAsync(Request(), "develop", TestContext.Current.CancellationToken));

        _requests.ShouldNotContain(request => request.Arguments[0] == "checkout");
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_KnownRefsWithTrackingBranch_CountsAgainstItsOldCommit()
    {
        _respond = request => request.Arguments switch
        {
            ["--no-optional-locks", "status", ..] => Output($"# branch.oid {_mainCommit}", "# branch.head main"),
            ["rev-list", ..] => Output("0"),
            _ => Result(0),
        };
        var known = new RefSnapshot([new("heads/main", _developCommit), new("tags/v1", _tagObject)]);

        await CreateClient().GetWorkingCopyStatusAsync(Request(), known, TestContext.Current.CancellationToken);

        _requests.Select(request => request.Arguments[0]).ShouldBe(["--no-optional-locks", "rev-list"]);
        _requests[^1].Arguments.ShouldBe(["rev-list", "--count", "HEAD", "--not", _developCommit, "--"]);
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_KnownRefsWithoutTrackingBranch_CountsAgainstEveryOldBranch()
    {
        _respond = request => request.Arguments switch
        {
            ["--no-optional-locks", "status", ..] => Output($"# branch.oid {_mainCommit}", "# branch.head feature"),
            ["rev-list", ..] => Output("1"),
            _ => Result(0),
        };
        var known = new RefSnapshot([new("heads/main", _mainCommit), new("heads/develop", _developCommit), new("tags/v1", _tagObject)]);

        var status = await CreateClient().GetWorkingCopyStatusAsync(Request(), known, TestContext.Current.CancellationToken);

        status.UnpushedCommits.ShouldBe(1);
        var arguments = _requests[^1].Arguments;
        arguments.Take(4).ShouldBe(["rev-list", "--count", "HEAD", "--not"]);
        arguments.Skip(4).Take(2).ShouldBe([_mainCommit, _developCommit], ignoreOrder: true);
        arguments[^1].ShouldBe("--");
    }

    [Theory]
    [InlineData(Operation.Clone)]
    [InlineData(Operation.Update)]
    [InlineData(Operation.LocalRefs)]
    public async Task Operation_AnyStep_DisablesHooksAndFileSystemMonitor(Operation operation)
    {
        await RunAsync(operation, Request());

        foreach (var request in _requests)
        {
            var variables = Variables(request);
            variables["GIT_CONFIG_KEY_3"].ShouldBe("core.hooksPath");
            variables["GIT_CONFIG_VALUE_3"].ShouldBe(Path.Combine(_clonePath, ".git", "githubbackup-no-hooks"));
            variables["GIT_CONFIG_KEY_4"].ShouldBe("core.fsmonitor");
            variables["GIT_CONFIG_VALUE_4"].ShouldBe("false");
            variables["GIT_CONFIG_KEY_5"].ShouldBe("core.protectNTFS");
            variables["GIT_CONFIG_VALUE_5"].ShouldBe("true");
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

        Variables(_requests.Single())["GIT_CONFIG_COUNT"].ShouldBe("6");
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
            Variables(request)["GIT_CONFIG_COUNT"].ShouldBe("6");
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
        variables["GIT_CONFIG_COUNT"].ShouldBe("8");
        variables["GIT_CONFIG_KEY_0"].ShouldBe("core.longpaths");
        variables["GIT_CONFIG_KEY_1"].ShouldBe("credential.helper");
        variables["GIT_CONFIG_KEY_2"].ShouldBe("core.symlinks");
        variables["GIT_CONFIG_KEY_6"].ShouldBe("protocol.file.allow");
        variables["GIT_CONFIG_VALUE_6"].ShouldBe("always");
        variables["GIT_CONFIG_KEY_7"].ShouldBe("http.https://github.com/.extraHeader");
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

    [Theory]
    [InlineData(Operation.Clone, "remote: Repository not found.")]
    [InlineData(Operation.Update, "remote: Repository not found.")]
    [InlineData(Operation.RemoteRefs, "fatal: 'C:/remotes/repo.wiki.git' does not appear to be a git repository")]
    [InlineData(Operation.DefaultBranch, "fatal: 'C:/remotes/repo.wiki.git' does not appear to be a git repository")]
    public async Task Operation_RepositoryMissingOnServer_ThrowsRepositoryNotFoundWithMaskedMessage(
        Operation operation, string marker)
    {
        _respond = _ => Result(128, marker, $"fatal: could not read from remote with {_token}");

        var exception = await Should.ThrowAsync<GitRepositoryNotFoundException>(() => RunAsync(operation, Request(_token)));

        exception.ExitCode.ShouldBe(128);
        exception.Message.ShouldContain(marker);
        exception.Message.ShouldContain(_clonePath);
        exception.Message.ShouldNotContain(_token);
    }

    [Theory]
    [InlineData(128, "fatal: Authentication failed")]
    [InlineData(128, "fatal: repository not found")]
    [InlineData(1, "remote: Repository not found.")]
    [InlineData(128, "fatal: 'origin' does not appear to be a git repository")]
    public async Task GetRemoteRefsAsync_OtherFailure_ThrowsGitCommandException(int exitCode, string error)
    {
        _respond = _ => Result(exitCode, error);

        var exception = await Should.ThrowAsync<GitCommandException>(
            () => CreateClient().GetRemoteRefsAsync(Request(), TestContext.Current.CancellationToken));

        exception.ShouldNotBeOfType<GitRepositoryNotFoundException>();
        exception.ExitCode.ShouldBe(exitCode);
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

    [Theory]
    [InlineData(false, "-ffdx")]
    [InlineData(true, "-ffd")]
    public async Task UpdateAsync_KeepIgnoredFiles_CleansWithoutOrWithIgnoredFiles(bool keepIgnoredFiles, string flags)
    {
        await CreateClient().UpdateAsync(
            Request() with { KeepIgnoredFiles = keepIgnoredFiles }, TestContext.Current.CancellationToken);

        _requests.Where(request => request.Arguments[0] == "clean").ShouldHaveSingleItem().Arguments.ShouldBe(["clean", flags]);
    }

    [Fact]
    public async Task GetOriginUrlAsync_OriginSet_ReturnsItFromLocalConfiguration()
    {
        _respond = _ => Output(_remoteUrl);

        var url = await CreateClient().GetOriginUrlAsync(Request(_token), TestContext.Current.CancellationToken);

        url.ShouldBe(_remoteUrl);
        var request = _requests.ShouldHaveSingleItem();
        request.Arguments.ShouldBe(["config", "--get", "remote.origin.url"]);
        request.WorkingDirectory.ShouldBe(_clonePath);
        request.EnvironmentVariables.ShouldAllBe(variable => !variable.IsSecret);
    }

    [Fact]
    public async Task GetOriginUrlAsync_NoOrigin_ReturnsNull()
    {
        _respond = _ => Result(1);

        var url = await CreateClient().GetOriginUrlAsync(Request(), TestContext.Current.CancellationToken);

        url.ShouldBeNull();
    }

    [Fact]
    public async Task GetOriginUrlAsync_OtherFailure_Throws()
    {
        _respond = _ => Result(128, "fatal: not a git repository");

        await Should.ThrowAsync<GitCommandException>(
            () => CreateClient().GetOriginUrlAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_BranchWithTrackingBranch_ReadsStatusWithoutLocksAndCountsAgainstIt()
    {
        _respond = request => request.Arguments switch
        {
            ["--no-optional-locks", "status", ..] => Output(
                $"# branch.oid {_mainCommit}", "# branch.head main", "1 .M N... 100644 100644 100644 a b file.txt", "? new.txt"),
            ["rev-list", ..] => Output("2"),
            _ => Result(0),
        };

        var status = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, TestContext.Current.CancellationToken);

        status.ShouldBe(new WorkingCopyStatus("main", true, true, true, 2));
        _requests.Select(request => request.Arguments).ShouldBe(
        [
            ["--no-optional-locks", "status", "--porcelain=v2", "--branch", "--untracked-files=normal", "--ignore-submodules=none"],
            ["rev-parse", "--verify", "--quiet", "refs/remotes/origin/main"],
            ["rev-list", "--count", "refs/remotes/origin/main..HEAD", "--"],
        ]);
    }

    [Theory]
    [InlineData("(detached)", 0)]
    [InlineData("feature", 1)]
    public async Task GetWorkingCopyStatusAsync_NoTrackingBranch_CountsAgainstEveryOriginBranch(string head, int expectedRevParse)
    {
        _respond = request => request.Arguments switch
        {
            ["--no-optional-locks", "status", ..] => Output($"# branch.oid {_mainCommit}", $"# branch.head {head}"),
            ["rev-parse", ..] => Result(1),
            ["rev-list", ..] => Output("4"),
            _ => Result(0),
        };

        var status = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, TestContext.Current.CancellationToken);

        status.UnpushedCommits.ShouldBe(4);
        _requests.Count(request => request.Arguments[0] == "rev-parse").ShouldBe(expectedRevParse);
        _requests[^1].Arguments.ShouldBe(["rev-list", "--count", "HEAD", "--not", "--remotes=origin", "--"]);
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_NoCommit_CountsNothing()
    {
        _respond = _ => Output("# branch.oid (initial)", "# branch.head main");

        var status = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, TestContext.Current.CancellationToken);

        status.ShouldBe(new WorkingCopyStatus("main", false, false, false, 0));
        _requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_StatusUnreadable_ThrowsGitCommandException()
    {
        _respond = _ => Output("garbage");

        await Should.ThrowAsync<GitCommandException>(
            () => CreateClient().GetWorkingCopyStatusAsync(Request(), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetIgnoredPathsAsync_PathsListed_ReturnsThemRelativeWithoutOnesOutsideClone()
    {
        _respond = request => request.Arguments switch
        {
            ["ls-files", "-z", "--stage"] => Output("100644 abc 0\tREADME.md\0"),
            _ => Output("bin/\0obj/Debug/\0../outside.txt\0" + @"C:\abs" + "\0settings.local.json\0"),
        };

        var paths = await CreateClient().GetIgnoredPathsAsync(Request(), TestContext.Current.CancellationToken);

        paths.ShouldBe(["bin", @"obj\Debug", "settings.local.json"]);
        _requests.Select(request => request.Arguments).ShouldBe(
        [
            ["ls-files", "-z", "--stage"],
            ["ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--directory"],
        ]);
    }

    [Fact]
    public async Task GetIgnoredPathsAsync_CheckedOutSubmodule_AddsItsPathsUnderItsFolder()
    {
        _fileSystem.AddFile(Path.Combine(_clonePath, "libs", "core", ".git"), new MockFileData("gitdir: x"));
        _respond = request => (request.Arguments, request.WorkingDirectory) switch
        {
            (["ls-files", "-z", "--stage"], var folder) when folder == _clonePath =>
                Output("160000 abc 0\tlibs/core\0160000 def 0\tlibs/gone\0100644 aaa 0\tREADME.md\0"),
            (["ls-files", "-z", "--stage"], _) => Output("100644 bbb 0\tlib.cs\0"),
            (["ls-files", ..], var folder) when folder == _clonePath => Output("bin/\0"),
            (["ls-files", ..], _) => Output("out/\0"),
            _ => Result(0),
        };

        var paths = await CreateClient().GetIgnoredPathsAsync(Request(), TestContext.Current.CancellationToken);

        paths.ShouldBe(["bin", @"libs\core\out"]);
    }

    [Fact]
    public async Task GetIgnoredPathsAsync_GitlinksPointBackOrOutside_ListsEachFolderOnceAndStops()
    {
        _fileSystem.AddFile(Path.Combine(_clonePath, ".git", "HEAD"), new MockFileData("ref"));
        _fileSystem.AddFile(Path.Combine(_clonePath, "libs", "core", ".git"), new MockFileData("gitdir: x"));
        _fileSystem.AddFile(Path.Combine(_parentPath, "sibling", ".git"), new MockFileData("gitdir: y"));
        _respond = request => request.Arguments switch
        {
            ["ls-files", "-z", "--stage"] => Output("160000 a 0\t.\0160000 b 0\t../sibling\0160000 c 0\t../core\0160000 d 0\tlibs/core\0"),
            _ => Output(),
        };

        await CreateClient().GetIgnoredPathsAsync(Request(), TestContext.Current.CancellationToken);

        _requests.Where(request => request.Arguments.Count == 3).Select(request => request.WorkingDirectory)
            .ShouldBe([_clonePath, Path.Combine(_clonePath, @"libs\core")]);
    }

    [Fact]
    public async Task GetIgnoredPathsAsync_DeeplyNestedSubmodules_StopsAtDepthLimit()
    {
        _respond = request =>
        {
            var folder = request.WorkingDirectory;
            AddSubmoduleFolder(folder, "s");
            return request.Arguments.Count == 3 ? Output("160000 a 0\ts\0") : Output();
        };

        await CreateClient().GetIgnoredPathsAsync(Request(), TestContext.Current.CancellationToken);

        _requests.Count(request => request.Arguments.Count == 3).ShouldBe(9);
    }

    [Fact]
    public async Task GetSymbolicLinkPathsAsync_LinksInCloneAndSubmodule_ReturnsThem()
    {
        _fileSystem.AddFile(Path.Combine(_clonePath, "libs", "core", ".git"), new MockFileData("gitdir: x"));
        _respond = request => request.WorkingDirectory == _clonePath
            ? Output("120000 a 0\tdocs/link\0160000 b 0\tlibs/core\0100644 c 0\tREADME.md\0")
            : Output("120000 d 0\tout\0");

        var links = await CreateClient().GetSymbolicLinkPathsAsync(Request(), TestContext.Current.CancellationToken);

        links.ShouldBe([@"docs\link", @"libs\core\out"]);
    }

    [Fact]
    public async Task GetUnpushedBranchCommitsAsync_LocalBranchAhead_CountsAgainstItsTrackingBranch()
    {
        _respond = request => request.Arguments switch
        {
            ["rev-list", ..] => Output("3"),
            _ => Result(0),
        };

        var count = await CreateClient().GetUnpushedBranchCommitsAsync(Request(), "dev", null, TestContext.Current.CancellationToken);

        count.ShouldBe(3);
        _requests.Select(request => request.Arguments).ShouldBe(
        [
            ["rev-parse", "--verify", "--quiet", "refs/heads/dev"],
            ["rev-parse", "--verify", "--quiet", "refs/remotes/origin/dev"],
            ["rev-list", "--count", "refs/remotes/origin/dev..refs/heads/dev", "--"],
        ]);
    }

    [Fact]
    public async Task GetUnpushedBranchCommitsAsync_NoLocalBranch_ReturnsZero()
    {
        _respond = _ => Result(1);

        var count = await CreateClient().GetUnpushedBranchCommitsAsync(Request(), "dev", null, TestContext.Current.CancellationToken);

        count.ShouldBe(0);
        _requests.ShouldHaveSingleItem();
    }

    /// <summary>
    /// Creates the folder <paramref name="name"/> with a <c>.git</c> file under <paramref name="parent"/>, so the
    /// client treats it as a checked-out submodule.
    /// </summary>
    /// <param name="parent">The parent folder.</param>
    /// <param name="name">The name of the submodule folder.</param>
    private void AddSubmoduleFolder(string parent, string name)
    {
        var gitFile = Path.Combine(parent, name, ".git");
        if (!_fileSystem.File.Exists(gitFile))
        {
            _fileSystem.AddFile(gitFile, new MockFileData("gitdir: x"));
        }
    }

    /// <summary>
    /// Returns whether a git command talks to the server and therefore gets the token.
    /// </summary>
    /// <param name="arguments">The git arguments.</param>
    /// <returns><see langword="true"/> for clone, fetch, ls-remote, submodule update and lfs.</returns>
    private static bool IsNetworkStep(IReadOnlyList<string> arguments) =>
        arguments[0] is "clone" or "fetch" or "ls-remote" or "lfs"
        || (arguments[0] == "submodule" && arguments[1] == "update");

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
    /// Returns the only log record with the event id <paramref name="eventId"/> and fails if there is not exactly one.
    /// </summary>
    /// <param name="eventId">The event id of the <see cref="LoggerMessageAttribute"/>.</param>
    /// <returns>The record.</returns>
    private FakeLogRecord SingleRecord(int eventId) =>
        _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId).ShouldHaveSingleItem();

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
