using GitHubBackup.Core.Runs;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="BackupRunFilter"/>.
/// </summary>
public sealed class BackupRunFilterTests
{
    private static readonly BackupWorkItem _accountItem =
        new("octocat", "tool", 1, null, @"C:\b\octocat\tool", @"C:\s\octocat\tool", true, @"C:\s\octocat\tool.wiki")
        {
            AccountLogin = "octocat",
        };

    private static readonly BackupWorkItem _standaloneItem =
        new("octocat", "solo", 2, null, @"C:\b\octocat\solo", @"C:\s\octocat\solo", true, @"C:\s\octocat\solo.wiki");

    [Fact]
    public void All_AnyRepository_IsIncludedAndDiscoveryRuns()
    {
        BackupRunFilter.All.IsRepository.ShouldBeFalse();
        BackupRunFilter.All.AccountLogin.ShouldBeNull();
        BackupRunFilter.All.Includes(_accountItem).ShouldBeTrue();
        BackupRunFilter.All.Includes(_standaloneItem).ShouldBeTrue();
    }

    [Fact]
    public void ForAccount_AccountEntryAndStandaloneOfSameOwner_IncludesOnlyAccountEntryIgnoringCase()
    {
        var filter = BackupRunFilter.ForAccount("OctoCat");

        filter.IsRepository.ShouldBeFalse();
        filter.Includes(_accountItem).ShouldBeTrue();
        filter.Includes(_standaloneItem).ShouldBeFalse();
    }

    [Fact]
    public void ForRepository_MatchingNameIgnoringCase_IncludesOnlyThatRepository()
    {
        var filter = BackupRunFilter.ForRepository("OCTOCAT", "Solo");

        filter.IsRepository.ShouldBeTrue();
        filter.RepositoryFullName.ShouldBe("OCTOCAT/Solo");
        filter.Includes(_standaloneItem).ShouldBeTrue();
        filter.Includes(_accountItem).ShouldBeFalse();
    }

    [Theory]
    [InlineData("all", false, false, false)]
    [InlineData("all", true, false, true)]
    [InlineData("all", false, true, true)]
    [InlineData("all", true, true, true)]
    [InlineData("account", false, false, false)]
    [InlineData("account", true, false, true)]
    [InlineData("account", false, true, false)]
    [InlineData("account", true, true, true)]
    [InlineData("repository", false, false, false)]
    [InlineData("repository", true, false, false)]
    [InlineData("repository", false, true, false)]
    [InlineData("repository", true, true, false)]
    public void Pauses_FilterAndPauseOfEntryAndAccount_SkipsByExplicitSelection(
        string scope, bool entryPaused, bool accountPaused, bool expected)
    {
        var filter = scope switch
        {
            "account" => BackupRunFilter.ForAccount("octocat"),
            "repository" => BackupRunFilter.ForRepository("octocat", "tool"),
            _ => BackupRunFilter.All,
        };
        var item = _accountItem with { IsPaused = entryPaused, IsAccountPaused = accountPaused };

        filter.Pauses(item).ShouldBe(expected);
    }

    [Fact]
    public void Pauses_PausedStandaloneEntry_SkippedOnlyWithoutRepositoryFilter()
    {
        var item = _standaloneItem with { IsPaused = true };

        BackupRunFilter.All.Pauses(item).ShouldBeTrue();
        BackupRunFilter.ForRepository("octocat", "solo").Pauses(item).ShouldBeFalse();
    }

    [Theory]
    [InlineData("", "repo")]
    [InlineData("owner", " ")]
    public void ForRepository_EmptyPart_Throws(string owner, string name) =>
        Should.Throw<ArgumentException>(() => BackupRunFilter.ForRepository(owner, name));

    [Fact]
    public void ForAccount_EmptyLogin_Throws() =>
        Should.Throw<ArgumentException>(() => BackupRunFilter.ForAccount(" "));
}
