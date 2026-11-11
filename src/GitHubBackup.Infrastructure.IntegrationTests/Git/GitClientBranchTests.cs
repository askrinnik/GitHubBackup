using GitHubBackup.Core.Git;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> on the server's default branch and the fallback to it when the requested branch is missing.
/// </summary>
public sealed class GitClientBranchTests : GitClientTestBase
{
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
        _clientLogger.Collector.GetSnapshot().ShouldNotContain(record => record.Level == LogLevel.Warning);
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
        var record = Records(_clientLogger, _branchNotFoundEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Branch").ShouldBe("missing");
        record.GetStructuredStateValue("DefaultBranch").ShouldBe("main");
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
        var record = Records(_clientLogger, _branchNotFoundEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Branch").ShouldBe("feature");
        record.GetStructuredStateValue("DefaultBranch").ShouldBe("main");
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
}
