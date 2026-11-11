using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Tests.GitHub;

/// <summary>
/// Tests <see cref="GitHubRemoteUrlProvider"/>.
/// </summary>
public sealed class GitHubRemoteUrlProviderTests
{
    private readonly GitHubRemoteUrlProvider _provider = new();

    [Theory]
    [InlineData("octocat", "Hello-World", "https://github.com/octocat/Hello-World.git")]
    [InlineData("my-org", "repo.name_1", "https://github.com/my-org/repo.name_1.git")]
    public void GetRepositoryUrl_ValidNames_ReturnsHttpsUrlWithGitSuffix(string owner, string name, string expected) =>
        _provider.GetRepositoryUrl(owner, name).ShouldBe(expected);

    [Theory]
    [InlineData("octo/cat", "repo", "owner")]
    [InlineData("user@evil.com", "repo", "owner")]
    [InlineData("octocat", "..", "name")]
    [InlineData("octocat", "repo/../../x", "name")]
    [InlineData("octocat", "", "name")]
    public void GetRepositoryUrl_InvalidName_Throws(string owner, string name, string parameter)
    {
        var exception = Should.Throw<ArgumentException>(() => _provider.GetRepositoryUrl(owner, name));

        exception.ParamName.ShouldBe(parameter);
    }
}
