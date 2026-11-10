using GitHubBackup.TestUtilities.Git;

namespace GitHubBackup.Infrastructure.IntegrationTests.TestInfrastructure;

/// <summary>
/// Tests <see cref="LocalGitSandbox"/>: remote creation, isolation from the machine's git configuration and cleanup.
/// </summary>
public sealed class LocalGitSandboxTests : IDisposable
{
    private readonly LocalGitSandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public async Task CreateRemoteAsync_NewRemote_CreatesBareRepositoryWithMainBranch()
    {
        var ct = TestContext.Current.CancellationToken;

        var remote = await _sandbox.CreateRemoteAsync("repo", ct);

        remote.Url.ShouldStartWith("file:///");
        var bare = await _sandbox.RunGitAsync(remote.BarePath, ct, "rev-parse", "--is-bare-repository");
        bare.ShouldBe(["true"]);
        var refs = await remote.GetRemoteRefsAsync(ct);
        refs.Keys.ShouldContain("refs/heads/main");
        refs.Keys.ShouldContain("HEAD");
    }

    [Fact]
    public async Task Clone_RemoteUrl_ChecksOutLatestCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        await remote.CommitAsync("data.txt", "hello", "Add data", ct);
        var target = Path.Combine(_sandbox.CreateFolder("clones"), "repo");

        await _sandbox.RunGitAsync(_sandbox.RootPath, ct, "clone", remote.Url, target);

        (await File.ReadAllTextAsync(Path.Combine(target, "data.txt"), ct)).ShouldBe("hello");
    }

    [Fact]
    public async Task CreateEmptyRemoteAsync_NewRemote_HasNoRefs()
    {
        var ct = TestContext.Current.CancellationToken;

        var remote = await _sandbox.CreateEmptyRemoteAsync("empty", ct);

        (await remote.GetRemoteRefsAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunGitAsync_CommandFails_ThrowsWithCommandAndError()
    {
        var ct = TestContext.Current.CancellationToken;

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => _sandbox.RunGitAsync(_sandbox.RootPath, ct, "rev-parse", "--verify", "refs/heads/missing"));

        exception.Message.ShouldContain("rev-parse --verify refs/heads/missing");
        exception.Message.ShouldContain("exit code 128");
        exception.Message.ShouldContain("fatal");
    }

    [Fact]
    public async Task RunGitAsync_GlobalConfig_IsIsolated()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = _sandbox.CreateFolder("config");

        var global = await _sandbox.RunGitAsync(folder, ct, "config", "--global", "--list");
        var identity = await _sandbox.RunGitAsync(folder, ct, "config", "--get", "user.name");
        var branch = await _sandbox.RunGitAsync(folder, ct, "config", "--get", "init.defaultBranch");

        global.ShouldBeEmpty();
        identity.ShouldBe(["Test User"]);
        branch.ShouldBe(["main"]);
    }

    [Fact]
    public async Task Dispose_ReadOnlyObjectFiles_DeletesRootFolder()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        foreach (var file in Directory.EnumerateFiles(remote.BarePath, "*", SearchOption.AllDirectories).Take(3))
        {
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }

        _sandbox.Dispose();

        Directory.Exists(_sandbox.RootPath).ShouldBeFalse();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _sandbox.Dispose();

        Should.NotThrow(_sandbox.Dispose);
    }
}
