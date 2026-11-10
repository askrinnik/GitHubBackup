using GitHubBackup.Core.Git;

namespace GitHubBackup.Core.Tests.Git;

/// <summary>
/// Tests <see cref="GitRepositoryRequest"/>.
/// </summary>
public sealed class GitRepositoryRequestTests
{
    [Fact]
    public void ToString_TokenSet_LeavesTokenOut()
    {
        var request = new GitRepositoryRequest
        {
            GitPath = @"C:\Git\cmd\git.exe",
            ClonePath = @"C:\backup\owner\repo",
            Branch = "main",
            Token = "ghp_secret-token-value",
        };

        request.ToString().ShouldBe(
            @"GitRepositoryRequest { GitPath = C:\Git\cmd\git.exe, ClonePath = C:\backup\owner\repo, Branch = main }");
    }
}
