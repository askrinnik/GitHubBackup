using System.IO.Abstractions;
using System.Text;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.IntegrationTests.Processes;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.TestUtilities.Git;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests <see cref="GitClient"/> with the installed git and git-lfs against local bare repositories.
/// </summary>
public sealed class GitClientTests : IDisposable
{
    private const string _token = "ghp_0123456789abcdefghijklmnopqrstuvwxyzAB";

    private readonly LocalGitSandbox _sandbox = new();
    private readonly SecretMasker _masker = new();
    private readonly CapturingLogger<ProcessRunner> _runnerLogger = new();
    private readonly CapturingLogger<GitClient> _clientLogger = new();
    private readonly string _clonePath;

    /// <summary>
    /// Creates the sandbox and chooses a clone folder whose parent does not exist yet.
    /// </summary>
    public GitClientTests() => _clonePath = Path.Combine(_sandbox.RootPath, "backup", "owner", "repo");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CloneAsync_RemoteWithBranchesAndTags_ChecksOutBranchAndFetchesEveryRef()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var head = await remote.CommitAsync("a.txt", "a", "Second commit", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.CreateTagAsync("v1", null, annotated: true, force: false, CancellationToken);
        await remote.CreateTagAsync("light", null, annotated: false, force: false, CancellationToken);

        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);

        (await HeadAsync(_clonePath)).ShouldBe(head);
        (await GitAsync(_clonePath, "rev-parse", "--abbrev-ref", "HEAD")).ShouldBe(["main"]);
        (await RefsAsync(_clonePath)).ShouldBe(
            ["refs/remotes/origin/HEAD", "refs/remotes/origin/develop", "refs/remotes/origin/main", "refs/tags/light", "refs/tags/v1"],
            ignoreOrder: true);
        File.ReadAllText(Path.Combine(_clonePath, "a.txt")).ShouldBe("a");
    }

    [Fact]
    public async Task CloneAsync_NonDefaultBranch_ChecksItOut()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.CheckoutAsync("develop", CancellationToken);
        var developHead = await remote.CommitAsync("dev.txt", "dev", "Develop commit", CancellationToken);

        await CreateClient().CloneAsync(Request("develop"), remote.Url, CancellationToken);

        (await HeadAsync(_clonePath)).ShouldBe(developHead);
        (await GitAsync(_clonePath, "rev-parse", "--abbrev-ref", "HEAD")).ShouldBe(["develop"]);
        File.Exists(Path.Combine(_clonePath, "dev.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateAsync_NewCommitOnServer_ChecksItOut()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        var head = await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        (await HeadAsync(_clonePath)).ShouldBe(head);
        File.ReadAllText(Path.Combine(_clonePath, "new.txt")).ShouldBe("new");
    }

    [Fact]
    public async Task UpdateAsync_ServerHistoryForcePushed_FollowsRewrittenHistory()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var initial = (await remote.GetRemoteRefsAsync(CancellationToken))["refs/heads/main"];
        await remote.CommitAsync("dropped.txt", "dropped", "Commit to drop", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        await remote.ForcePushAsync("main", initial, CancellationToken);
        var head = await remote.CommitAsync("rewritten.txt", "rewritten", "Rewritten commit", CancellationToken);

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        (await HeadAsync(_clonePath)).ShouldBe(head);
        File.Exists(Path.Combine(_clonePath, "dropped.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(_clonePath, "rewritten.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateAsync_LocalChanges_DiscardsModifiedUntrackedAndIgnoredFiles()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CommitAsync(".gitignore", "*.log\n", "Ignore logs", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        File.WriteAllText(Path.Combine(_clonePath, "README.md"), "changed");
        File.WriteAllText(Path.Combine(_clonePath, "untracked.txt"), "untracked");
        Directory.CreateDirectory(Path.Combine(_clonePath, "untracked-folder"));
        File.WriteAllText(Path.Combine(_clonePath, "untracked-folder", "file.txt"), "untracked");
        File.WriteAllText(Path.Combine(_clonePath, "build.log"), "ignored");

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        File.ReadAllText(Path.Combine(_clonePath, "README.md")).ShouldBe("repo");
        File.Exists(Path.Combine(_clonePath, "untracked.txt")).ShouldBeFalse();
        Directory.Exists(Path.Combine(_clonePath, "untracked-folder")).ShouldBeFalse();
        File.Exists(Path.Combine(_clonePath, "build.log")).ShouldBeFalse();
        (await GitAsync(_clonePath, "status", "--porcelain", "--ignored")).ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_BranchDeletedAndTagsChanged_MirrorsServerRefs()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("feature", null, CancellationToken);
        await remote.CreateTagAsync("moved", null, annotated: false, force: false, CancellationToken);
        await remote.CreateTagAsync("gone", null, annotated: false, force: false, CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        var head = await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);
        await remote.CreateTagAsync("moved", null, annotated: false, force: true, CancellationToken);
        await remote.DeleteTagAsync("gone", CancellationToken);
        await remote.DeleteBranchAsync("feature", CancellationToken);

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        (await RefsAsync(_clonePath)).ShouldBe(
            ["refs/remotes/origin/HEAD", "refs/remotes/origin/main", "refs/tags/moved"], ignoreOrder: true);
        (await GitAsync(_clonePath, "rev-parse", "refs/tags/moved")).ShouldBe([head]);
    }

    [Fact]
    public async Task CloneAsync_NestedSubmodules_ChecksOutEveryLevel()
    {
        var leaf = await _sandbox.CreateRemoteAsync("leaf", CancellationToken);
        var middle = await _sandbox.CreateRemoteAsync("middle", CancellationToken);
        await middle.AddSubmoduleAsync(leaf, "leaf", CancellationToken);
        var top = await _sandbox.CreateRemoteAsync("top", CancellationToken);
        await top.AddSubmoduleAsync(middle, "middle", CancellationToken);

        await CreateClient().CloneAsync(Request(), top.Url, CancellationToken);

        File.ReadAllText(Path.Combine(_clonePath, "middle", "README.md")).ShouldBe("middle");
        File.ReadAllText(Path.Combine(_clonePath, "middle", "leaf", "README.md")).ShouldBe("leaf");
    }

    [Fact]
    public async Task UpdateAsync_SubmoduleAdvanced_ChecksOutNewSubmoduleCommit()
    {
        var submodule = await _sandbox.CreateRemoteAsync("sub", CancellationToken);
        var top = await _sandbox.CreateRemoteAsync("top", CancellationToken);
        await top.AddSubmoduleAsync(submodule, "sub", CancellationToken);
        await CreateClient().CloneAsync(Request(), top.Url, CancellationToken);
        var submoduleHead = await submodule.CommitAsync("new.txt", "new", "Submodule commit", CancellationToken);
        await _sandbox.RunGitAsync(top.WorkingPath, CancellationToken, "submodule", "update", "--remote", "sub");
        await _sandbox.RunGitAsync(top.WorkingPath, CancellationToken, "commit", "-am", "Advance submodule");
        await _sandbox.RunGitAsync(top.WorkingPath, CancellationToken, "push", "origin", "HEAD");

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        (await HeadAsync(Path.Combine(_clonePath, "sub"))).ShouldBe(submoduleHead);
        File.Exists(Path.Combine(_clonePath, "sub", "new.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task CloneAndUpdate_TokenSet_LeaveNoTokenInConfigsOrLog()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + _token));
        var submodule = await _sandbox.CreateRemoteAsync("sub", CancellationToken);
        var top = await _sandbox.CreateRemoteAsync("top", CancellationToken);
        await top.AddSubmoduleAsync(submodule, "sub", CancellationToken);

        await CreateClient().CloneAsync(Request(branch: null, token: _token), top.Url, CancellationToken);
        await CreateClient().UpdateAsync(Request(branch: "missing", token: _token), CancellationToken);
        await CreateClient().GetRemoteRefsAsync(Request(token: _token), CancellationToken);
        await CreateClient().GetDefaultBranchAsync(Request(token: _token), CancellationToken);
        _runnerLogger.Entries.ShouldContain(entry => entry.Message.Contains("ls-remote"));

        var configs = Directory.EnumerateFiles(Path.Combine(_clonePath, ".git", "modules"), "config", SearchOption.AllDirectories)
            .Prepend(Path.Combine(_clonePath, ".git", "config"))
            .ToList();
        configs.Count.ShouldBe(2);
        foreach (var config in configs)
        {
            var content = File.ReadAllText(config);
            content.ShouldNotContain(_token);
            content.ShouldNotContain(encoded);
            content.ShouldNotContain("extraheader", Case.Insensitive);
        }

        var logged = _runnerLogger.Entries.Concat(_clientLogger.Entries)
            .SelectMany(entry => entry.Properties.Values.Select(value => value?.ToString() ?? string.Empty).Prepend(entry.Message))
            .ToList();
        logged.ShouldNotBeEmpty();
        logged.ShouldAllBe(text => !text.Contains(_token) && !text.Contains(encoded));
    }

    [Fact]
    public async Task CloneAsync_PathLongerThanMaxPath_ChecksOutEveryFile()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var segment = new string('d', 60);
        var relativePath = Path.Combine(segment + "1", segment + "2", segment + "3", segment + "4", "file.txt");
        await remote.CommitAsync(relativePath, "deep", "Deep file", CancellationToken);

        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);

        var fullPath = Path.Combine(_clonePath, relativePath);
        fullPath.Length.ShouldBeGreaterThan(260);
        File.ReadAllText(fullPath).ShouldBe("deep");
    }

    [Fact]
    public async Task CloneAsync_RemoteMissing_ThrowsGitRepositoryNotFoundException()
    {
        var url = new Uri(Path.Combine(_sandbox.RootPath, "remotes", "missing.git")).AbsoluteUri;

        var exception = await Should.ThrowAsync<GitRepositoryNotFoundException>(
            () => CreateClient().CloneAsync(Request(), url, CancellationToken));

        exception.Command.ShouldStartWith("git clone");
        exception.ExitCode.ShouldNotBe(0);
        Directory.Exists(_clonePath).ShouldBeFalse();
    }

    [Fact]
    public async Task GetRemoteRefsAsync_RemoteDeletedAfterClone_ThrowsGitRepositoryNotFoundException()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CommitAsync("a.txt", "a", "Initial commit", CancellationToken);
        var client = CreateClient();
        await client.CloneAsync(Request(), remote.Url, CancellationToken);
        var missing = new Uri(Path.Combine(_sandbox.RootPath, "remotes", "missing.git")).AbsoluteUri;
        await client.SetRemoteUrlAsync(Request(), missing, CancellationToken);

        var exception = await Should.ThrowAsync<GitRepositoryNotFoundException>(
            () => client.GetRemoteRefsAsync(Request(), CancellationToken));

        exception.Command.ShouldStartWith("git ls-remote");
        File.Exists(Path.Combine(_clonePath, "a.txt")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("update")]
    [InlineData("local")]
    [InlineData("remote")]
    [InlineData("default")]
    public async Task Operation_ClonePathWithoutGitInsideAnotherRepository_ThrowsGitCommandException(string operation)
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CommitAsync("a.txt", "a", "Initial commit", CancellationToken);
        var enclosingRepository = Path.GetDirectoryName(Path.GetDirectoryName(_clonePath))!;
        Directory.CreateDirectory(_clonePath);
        await GitAsync(enclosingRepository, "init", "-b", "main");
        await GitAsync(enclosingRepository, "remote", "add", "origin", remote.Url);
        var client = CreateClient();

        var exception = await Should.ThrowAsync<GitCommandException>(() => operation switch
        {
            "update" => client.UpdateAsync(Request(), CancellationToken),
            "local" => client.GetLocalRefsAsync(Request(), CancellationToken),
            "remote" => client.GetRemoteRefsAsync(Request(), CancellationToken),
            _ => client.GetDefaultBranchAsync(Request(), CancellationToken),
        });

        exception.ExitCode.ShouldNotBe(0);
        Directory.Exists(Path.Combine(_clonePath, ".git")).ShouldBeFalse();
    }

    [Fact]
    public async Task Snapshots_OneServerState_RemoteAndLocalMatch()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.CreateBranchAsync("feature/nested", null, CancellationToken);
        await remote.CreateTagAsync("v1", null, annotated: true, force: false, CancellationToken);
        await remote.CreateTagAsync("light", null, annotated: false, force: false, CancellationToken);
        await _sandbox.RunGitAsync(remote.WorkingPath, CancellationToken, "push", "origin", "HEAD:refs/pull/1/head");
        var client = CreateClient();

        var result = await client.CloneAsync(Request(), remote.Url, CancellationToken);
        var remoteRefs = await client.GetRemoteRefsAsync(Request(), CancellationToken);
        var localRefs = await client.GetLocalRefsAsync(Request(), CancellationToken);

        remoteRefs.ShouldBe(localRefs);
        result.Refs.ShouldBe(localRefs);
        remoteRefs.Refs.Keys.ShouldBe(
            ["heads/develop", "heads/feature/nested", "heads/main", "tags/light", "tags/v1"], ignoreOrder: true);
        (await remote.GetRemoteRefsAsync(CancellationToken)).ShouldContainKey("refs/pull/1/head");
    }

    [Fact]
    public async Task Snapshots_AnnotatedTag_BothMapItToTheTagObject()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateTagAsync("v1", null, annotated: true, force: false, CancellationToken);
        var tagObject = (await _sandbox.RunGitAsync(remote.BarePath, CancellationToken, "rev-parse", "refs/tags/v1")).Single();
        var commit = (await _sandbox.RunGitAsync(remote.BarePath, CancellationToken, "rev-parse", "refs/tags/v1^{commit}")).Single();
        var client = CreateClient();

        await client.CloneAsync(Request(), remote.Url, CancellationToken);
        var remoteRefs = await client.GetRemoteRefsAsync(Request(), CancellationToken);
        var localRefs = await client.GetLocalRefsAsync(Request(), CancellationToken);

        tagObject.ShouldNotBe(commit);
        remoteRefs.Refs["tags/v1"].ShouldBe(tagObject);
        localRefs.Refs["tags/v1"].ShouldBe(tagObject);
    }

    [Fact]
    public async Task Snapshots_ServerRefsChanged_DifferBeforeUpdateAndMatchAfter()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("feature", null, CancellationToken);
        await remote.CreateTagAsync("moved", null, annotated: false, force: false, CancellationToken);
        await remote.CreateTagAsync("gone", null, annotated: true, force: false, CancellationToken);
        var client = CreateClient();
        await client.CloneAsync(Request(), remote.Url, CancellationToken);
        await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);
        await remote.CreateTagAsync("moved", null, annotated: false, force: true, CancellationToken);
        await remote.DeleteTagAsync("gone", CancellationToken);
        await remote.DeleteBranchAsync("feature", CancellationToken);
        await remote.CreateBranchAsync("added", null, CancellationToken);

        var remoteBefore = await client.GetRemoteRefsAsync(Request(), CancellationToken);
        var localBefore = await client.GetLocalRefsAsync(Request(), CancellationToken);
        var result = await client.UpdateAsync(Request(), CancellationToken);
        var localAfter = await client.GetLocalRefsAsync(Request(), CancellationToken);

        remoteBefore.ShouldNotBe(localBefore);
        localAfter.ShouldBe(remoteBefore);
        result.Refs.ShouldBe(localAfter);
        localAfter.Refs.Keys.ShouldBe(["heads/added", "heads/main", "tags/moved"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetDefaultBranchAsync_ServerHeadOnDevelop_ReturnsDevelop()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        await remote.SetDefaultBranchAsync("develop", CancellationToken);

        var branch = await CreateClient().GetDefaultBranchAsync(Request(), CancellationToken);

        branch.ShouldBe("develop");
    }

    [Fact]
    public async Task CloneAsync_BranchNotSet_ChecksOutServerDefault()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.CheckoutAsync("develop", CancellationToken);
        var developHead = await remote.CommitAsync("dev.txt", "dev", "Develop commit", CancellationToken);
        await remote.SetDefaultBranchAsync("develop", CancellationToken);

        var result = await CreateClient().CloneAsync(Request(branch: null), remote.Url, CancellationToken);

        result.Branch.ShouldBe("develop");
        result.BranchFallback.ShouldBeFalse();
        (await HeadAsync(_clonePath)).ShouldBe(developHead);
        (await GitAsync(_clonePath, "rev-parse", "--abbrev-ref", "HEAD")).ShouldBe(["develop"]);
        _clientLogger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task UpdateAsync_ServerDefaultBranchChanged_ChecksOutNewDefault()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.CheckoutAsync("develop", CancellationToken);
        var developHead = await remote.CommitAsync("dev.txt", "dev", "Develop commit", CancellationToken);
        var first = await CreateClient().CloneAsync(Request(branch: null), remote.Url, CancellationToken);
        await remote.SetDefaultBranchAsync("develop", CancellationToken);

        var result = await CreateClient().UpdateAsync(Request(branch: null), CancellationToken);

        first.Branch.ShouldBe("main");
        result.Branch.ShouldBe("develop");
        result.BranchFallback.ShouldBeFalse();
        (await HeadAsync(_clonePath)).ShouldBe(developHead);
        File.Exists(Path.Combine(_clonePath, "dev.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task CloneAsync_BranchMissing_FallsBackToDefaultWithWarning()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var head = (await remote.GetRemoteRefsAsync(CancellationToken))["refs/heads/main"];

        var result = await CreateClient().CloneAsync(Request("missing"), remote.Url, CancellationToken);

        result.Branch.ShouldBe("main");
        result.BranchFallback.ShouldBeTrue();
        result.IsEmpty.ShouldBeFalse();
        (await HeadAsync(_clonePath)).ShouldBe(head);
        _clientLogger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("missing") && entry.Message.Contains("main"));
    }

    [Fact]
    public async Task UpdateAsync_BranchDeletedOnServer_FallsBackToDefaultWithWarning()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("feature", null, CancellationToken);
        await CreateClient().CloneAsync(Request("feature"), remote.Url, CancellationToken);
        await remote.DeleteBranchAsync("feature", CancellationToken);
        var head = await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);

        var result = await CreateClient().UpdateAsync(Request("feature"), CancellationToken);

        result.Branch.ShouldBe("main");
        result.BranchFallback.ShouldBeTrue();
        (await HeadAsync(_clonePath)).ShouldBe(head);
        (await GitAsync(_clonePath, "rev-parse", "--abbrev-ref", "HEAD")).ShouldBe(["main"]);
        _clientLogger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("feature"));
    }

    [Fact]
    public async Task UpdateAsync_BranchAndServerHeadMissing_ThrowsBranchNotFound()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        await remote.SetDefaultBranchAsync("nowhere", CancellationToken);

        var exception = await Should.ThrowAsync<GitBranchNotFoundException>(
            () => CreateClient().UpdateAsync(Request("missing"), CancellationToken));

        exception.RequestedBranch.ShouldBe("missing");
        exception.DefaultBranch.ShouldBeNull();
        exception.Message.ShouldContain(_clonePath);
    }

    [Fact]
    public async Task CloneAsync_EmptyRepository_ClonesWithoutCheckoutAndReportsEmpty()
    {
        var remote = await _sandbox.CreateEmptyRemoteAsync("repo", CancellationToken);

        var result = await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);

        result.IsEmpty.ShouldBeTrue();
        result.Branch.ShouldBeNull();
        result.BranchFallback.ShouldBeFalse();
        result.Refs.ShouldBe(RefSnapshot.Empty);
        Directory.Exists(Path.Combine(_clonePath, ".git")).ShouldBeTrue();
        _clientLogger.Entries.ShouldContain(entry => entry.Message.Contains("is empty on the server"));
        (await CreateClient().GetRemoteRefsAsync(Request(), CancellationToken)).ShouldBe(RefSnapshot.Empty);
        (await CreateClient().GetDefaultBranchAsync(Request(), CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task UpdateAsync_EmptyRepository_ReportsEmpty()
    {
        var remote = await _sandbox.CreateEmptyRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(branch: null), remote.Url, CancellationToken);

        var result = await CreateClient().UpdateAsync(Request(branch: null), CancellationToken);

        result.IsEmpty.ShouldBeTrue();
        result.Refs.ShouldBe(RefSnapshot.Empty);
        _clientLogger.Entries.Count(entry => entry.Message.Contains("is empty on the server")).ShouldBe(2);
    }

    [Fact]
    public async Task UpdateAsync_FirstCommitPushedToEmptyRepository_ChecksItOut()
    {
        var remote = await _sandbox.CreateEmptyRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        var head = await remote.CommitAsync("first.txt", "first", "First commit", CancellationToken);

        var result = await CreateClient().UpdateAsync(Request(), CancellationToken);

        result.IsEmpty.ShouldBeFalse();
        result.Branch.ShouldBe("main");
        (await HeadAsync(_clonePath)).ShouldBe(head);
        File.ReadAllText(Path.Combine(_clonePath, "first.txt")).ShouldBe("first");
    }

    /// <inheritdoc />
    [Fact]
    public async Task SetRemoteUrlAsync_ExistingClone_ChangesOrigin()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        var renamed = await _sandbox.CreateRemoteAsync("renamed", CancellationToken);

        await CreateClient().SetRemoteUrlAsync(Request(), renamed.Url, CancellationToken);

        (await GitAsync(_clonePath, "remote", "get-url", "origin")).ShouldBe([renamed.Url]);
        _clientLogger.Entries.ShouldContain(entry => entry.Message.Contains(renamed.Url));
    }

    [Theory]
    [InlineData("--upload-pack=evil")]
    [InlineData(" ")]
    public async Task SetRemoteUrlAsync_InvalidUrl_ThrowsWithoutRunningGit(string url)
    {
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => CreateClient().SetRemoteUrlAsync(Request(), url, CancellationToken));

        exception.ParamName.ShouldBe("remoteUrl");
        _runnerLogger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetRemoteUrlAsync_ClonePathNotFullyQualified_Throws()
    {
        var request = Request() with { ClonePath = @"owner\repo" };

        var exception = await Should.ThrowAsync<ArgumentException>(
            () => CreateClient().SetRemoteUrlAsync(request, "https://github.com/octo/repo.git", CancellationToken));

        exception.ParamName.ShouldBe("request");
    }

    public void Dispose() => _sandbox.Dispose();

    /// <summary>
    /// Creates a client over the real file system and the real process runner, allowing submodules from
    /// <c>file://</c> remotes.
    /// </summary>
    /// <returns>The client under test.</returns>
    private GitClient CreateClient()
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
    private GitRepositoryRequest Request(string? branch = "main", string? token = null) => new()
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
    private Task<IReadOnlyList<string>> GitAsync(string workingDirectory, params string[] arguments) =>
        _sandbox.RunGitAsync(workingDirectory, CancellationToken, arguments);

    /// <summary>
    /// Returns the commit checked out in <paramref name="workingDirectory"/>.
    /// </summary>
    /// <param name="workingDirectory">The clone or submodule folder.</param>
    /// <returns>The commit id.</returns>
    private async Task<string> HeadAsync(string workingDirectory) =>
        (await GitAsync(workingDirectory, "rev-parse", "HEAD")).Single();

    /// <summary>
    /// Returns the names of the remote-tracking branches and tags of a clone.
    /// </summary>
    /// <param name="workingDirectory">The clone folder.</param>
    /// <returns>The full ref names.</returns>
    private Task<IReadOnlyList<string>> RefsAsync(string workingDirectory) =>
        GitAsync(workingDirectory, "for-each-ref", "--format=%(refname)", "refs/remotes", "refs/tags");
}
