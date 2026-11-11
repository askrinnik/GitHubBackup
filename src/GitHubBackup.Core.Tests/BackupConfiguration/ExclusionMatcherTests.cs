using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Tests.BackupConfiguration;

/// <summary>
/// Tests <see cref="ExclusionMatcher"/>.
/// </summary>
public sealed class ExclusionMatcherTests
{
    private readonly ExclusionMatcher _matcher = new();
    private readonly BackupConfig _config = new();

    [Fact]
    public void Match_NoMasks_ReturnsNull()
    {
        _config.Accounts.Add(new() { Url = "https://github.com/octocat" });

        _matcher.Match(_config, "octocat", "repo").ShouldBeNull();
    }

    [Fact]
    public void Match_AccountMaskMatchesName_ReturnsAccountMatch()
    {
        AddAccount("octocat", "test-*");

        _matcher.Match(_config, "octocat", "test-app").ShouldBe(new("test-*", ExclusionScope.Account));
    }

    [Fact]
    public void Match_AccountMaskDoesNotMatchName_ReturnsNull()
    {
        AddAccount("octocat", "test-*");

        _matcher.Match(_config, "octocat", "app").ShouldBeNull();
    }

    [Fact]
    public void Match_OwnerInOtherCase_StillUsesAccountMasks()
    {
        AddAccount("OctoCat", "old");

        _matcher.Match(_config, "octocat", "OLD").ShouldBe(new("old", ExclusionScope.Account));
    }

    [Fact]
    public void Match_MaskOfOtherAccount_IsNotApplied()
    {
        AddAccount("octocat", "*");
        AddAccount("hubot");

        _matcher.Match(_config, "hubot", "repo").ShouldBeNull();
    }

    [Fact]
    public void Match_AccountMaskWithOwnerPrefix_DoesNotMatch()
    {
        AddAccount("octocat", "octocat/*");

        _matcher.Match(_config, "octocat", "repo").ShouldBeNull();
    }

    [Fact]
    public void Match_GlobalMaskMatchesFullName_ReturnsGlobalMatch()
    {
        _config.Exclude.Add("octocat/*-archive");

        _matcher.Match(_config, "octocat", "old-archive").ShouldBe(new("octocat/*-archive", ExclusionScope.Global));
        _matcher.Match(_config, "hubot", "old-archive").ShouldBeNull();
    }

    [Fact]
    public void Match_GlobalMaskWithoutSlash_MatchesNothing()
    {
        _config.Exclude.Add("repo");

        _matcher.Match(_config, "octocat", "repo").ShouldBeNull();
    }

    [Fact]
    public void Match_GlobalStar_ExcludesEverything()
    {
        _config.Exclude.Add("*");

        _matcher.Match(_config, "anyone", "anything").ShouldBe(new("*", ExclusionScope.Global));
    }

    [Fact]
    public void Match_BothScopesMatch_ReturnsAccountMask()
    {
        AddAccount("octocat", "repo");
        _config.Exclude.Add("*/repo");

        _matcher.Match(_config, "octocat", "repo").ShouldBe(new("repo", ExclusionScope.Account));
    }

    [Fact]
    public void Match_FirstMatchingMaskInFileOrder_IsReturned()
    {
        AddAccount("octocat", "x*", "re*", "repo");

        _matcher.Match(_config, "octocat", "repo").ShouldBe(new("re*", ExclusionScope.Account));
    }

    [Fact]
    public void Match_OwnerWithoutAccount_UsesOnlyGlobalMasks()
    {
        AddAccount("octocat", "*");
        _config.Exclude.Add("hubot/secret");

        _matcher.Match(_config, "hubot", "secret").ShouldBe(new("hubot/secret", ExclusionScope.Global));
        _matcher.Match(_config, "hubot", "public").ShouldBeNull();
    }

    [Fact]
    public void Match_AccountWithInvalidUrl_IsIgnored()
    {
        _config.Accounts.Add(new() { Url = "not a url", Exclude = ["*"] });

        _matcher.Match(_config, "octocat", "repo").ShouldBeNull();
    }

    /// <summary>
    /// Adds an account with <paramref name="masks"/> as its exclusion masks.
    /// </summary>
    /// <param name="login">The account login.</param>
    /// <param name="masks">The masks.</param>
    private void AddAccount(string login, params string[] masks) =>
        _config.Accounts.Add(new() { Url = $"https://github.com/{login}", Exclude = [.. masks] });
}
