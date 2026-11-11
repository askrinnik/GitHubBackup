using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Runs;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="BackupWorkItem"/>.
/// </summary>
public sealed class BackupWorkItemTests
{
    private readonly RepositoryPathResolver _resolver = new(@"C:\app\sources");

    [Fact]
    public void CreateAll_AccountsAndStandaloneRepositories_ListsAccountRepositoriesFirstInFileOrder()
    {
        var config = new BackupConfig
        {
            BackupRoot = @"C:\backup",
            SourcesRoot = @"D:\clones",
            Accounts =
            [
                new()
                {
                    Url = "https://github.com/octocat",
                    Repositories =
                    [
                        new() { Name = "b-repo", Id = 2, Branch = "dev" },
                        new() { Name = "a-repo", Id = 1, Status = RepositoryStatus.Unavailable },
                    ],
                },
            ],
            Repositories =
            [
                new() { Url = "https://github.com/someone/tool", Id = 3, Path = @"E:\archives\tool" },
            ],
        };

        var items = BackupWorkItem.CreateAll(config, _resolver);

        items.ShouldBe(
        [
            new("octocat", "b-repo", 2, "dev", @"C:\backup\octocat\b-repo", @"D:\clones\octocat\b-repo", true, @"D:\clones\octocat\b-repo.wiki")
            {
                AccountLogin = "octocat",
            },
            new("octocat", "a-repo", 1, null, @"C:\backup\octocat\a-repo", @"D:\clones\octocat\a-repo", false, @"D:\clones\octocat\a-repo.wiki")
            {
                AccountLogin = "octocat",
            },
            new("someone", "tool", 3, null, @"E:\archives\tool", @"D:\clones\someone\tool", true, @"D:\clones\someone\tool.wiki"),
        ]);
        items[2].FullName.ShouldBe("someone/tool");
        items[2].WikiFullName.ShouldBe("someone/tool.wiki");
    }

    [Fact]
    public void CreateAll_EmptyConfig_ReturnsNoItems() =>
        BackupWorkItem.CreateAll(new BackupConfig { BackupRoot = @"C:\backup" }, _resolver).ShouldBeEmpty();

    [Fact]
    public void CreateAll_InvalidRepositoryUrl_Throws()
    {
        var config = new BackupConfig
        {
            BackupRoot = @"C:\backup",
            Repositories = [new() { Url = "https://example.com/owner/repo", Id = 1 }],
        };

        Should.Throw<ArgumentException>(() => BackupWorkItem.CreateAll(config, _resolver));
    }
}
