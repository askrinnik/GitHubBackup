using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Tests.BackupConfiguration;

/// <summary>
/// Tests <see cref="RepositoryPathResolver"/>.
/// </summary>
public sealed class RepositoryPathResolverTests
{
    private const string _defaultSourcesRoot = @"C:\app\sources";

    private readonly RepositoryPathResolver _resolver = new(_defaultSourcesRoot);

    [Theory]
    [InlineData(null, null, @"C:\Backups\askrinnik\my-repo")]
    [InlineData(@"E:\Work\Mine", null, @"E:\Work\Mine\my-repo")]
    [InlineData(null, @"F:\Special\my-repo", @"F:\Special\my-repo")]
    [InlineData(@"E:\Work\Mine", @"F:\Special\my-repo", @"F:\Special\my-repo")]
    public void GetArchiveFolder_AccountRepositoryPathCombinations_AppliesOverrides(
        string? accountPath, string? repositoryPath, string expected)
    {
        var account = Account("https://github.com/askrinnik", accountPath);
        var repository = new AccountRepositoryConfig { Name = "my-repo", Id = 1, Path = repositoryPath };

        _resolver.GetArchiveFolder(Config(), account, repository).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, @"C:\Backups\dotnet\runtime")]
    [InlineData(@"F:\Special\runtime", @"F:\Special\runtime")]
    public void GetArchiveFolder_StandaloneRepositoryPathCombinations_AppliesOverride(
        string? repositoryPath, string expected) =>
        _resolver.GetArchiveFolder(Config(), Standalone("https://github.com/dotnet/runtime", repositoryPath))
            .ShouldBe(expected);

    [Theory]
    [InlineData("https://github.com/askrinnik/my-repo", @"C:\Backups\askrinnik\my-repo")]
    [InlineData("https://github.com/dotnet/runtime", @"C:\Backups\dotnet\runtime")]
    public void GetArchiveFolder_OwnAndForeignStandaloneRepositories_UseSameRule(string url, string expected) =>
        _resolver.GetArchiveFolder(Config(), Standalone(url, null)).ShouldBe(expected);

    [Fact]
    public void GetArchiveFolder_AccountAndStandaloneRepositoryOfSameOwner_ReturnSameFolder()
    {
        var config = Config();
        var account = Account("https://github.com/dotnet", null);
        var accountRepository = new AccountRepositoryConfig { Name = "runtime", Id = 1 };

        var fromAccount = _resolver.GetArchiveFolder(config, account, accountRepository);
        var standalone = _resolver.GetArchiveFolder(config, Standalone("https://github.com/dotnet/runtime", null));

        fromAccount.ShouldBe(standalone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://gitlab.com/askrinnik")]
    [InlineData("https://github.com/askrinnik/my-repo")]
    public void GetArchiveFolder_InvalidAccountUrl_Throws(string url) =>
        Should.Throw<ArgumentException>(() => _resolver.GetArchiveFolder(
            Config(), Account(url, null), new AccountRepositoryConfig { Name = "my-repo", Id = 1 }));

    [Theory]
    [InlineData("")]
    [InlineData("https://github.com/askrinnik")]
    [InlineData("https://github.com/askrinnik/..")]
    public void GetArchiveFolder_InvalidRepositoryUrl_Throws(string url) =>
        Should.Throw<ArgumentException>(() => _resolver.GetArchiveFolder(Config(), Standalone(url, null)));

    [Theory]
    [InlineData("..")]
    [InlineData(@"..\..\Windows")]
    public void GetArchiveFolder_AccountRepositoryNameEscapingRoot_Throws(string name) =>
        Should.Throw<ArgumentException>(() => _resolver.GetArchiveFolder(
            Config(), Account("https://github.com/askrinnik", null), new AccountRepositoryConfig { Name = name, Id = 1 }));

    [Theory]
    [InlineData(null, _defaultSourcesRoot)]
    [InlineData(@"D:\Clones", @"D:\Clones")]
    public void GetSourcesRoot_SourcesRootSetOrNot_ReturnsConfiguredOrDefault(string? sourcesRoot, string expected) =>
        _resolver.GetSourcesRoot(Config(sourcesRoot)).ShouldBe(expected);

    [Theory]
    [InlineData(null, @"C:\app\sources\askrinnik\my-repo")]
    [InlineData(@"D:\Clones", @"D:\Clones\askrinnik\my-repo")]
    public void GetCloneFolder_SourcesRootSetOrNot_ReturnsOwnerAndRepositoryUnderRoot(string? sourcesRoot, string expected) =>
        _resolver.GetCloneFolder(Config(sourcesRoot), "askrinnik", "my-repo", null).ShouldBe(expected);

    [Theory]
    [InlineData(null, @"C:\app\sources\askrinnik\my-repo.wiki")]
    [InlineData(@"D:\Clones", @"D:\Clones\askrinnik\my-repo.wiki")]
    public void GetWikiCloneFolder_SourcesRootSetOrNot_ReturnsWikiFolderNextToClone(string? sourcesRoot, string expected) =>
        _resolver.GetWikiCloneFolder(Config(sourcesRoot), "askrinnik", "my-repo", null).ShouldBe(expected);

    [Theory]
    [InlineData(null, @"C:\app\sources\.tmp")]
    [InlineData(@"D:\Clones", @"D:\Clones\.tmp")]
    public void GetTemporaryArchiveFolder_SourcesRootSetOrNot_ReturnsTmpUnderRoot(string? sourcesRoot, string expected) =>
        _resolver.GetTemporaryArchiveFolder(Config(sourcesRoot)).ShouldBe(expected);

    [Fact]
    public void GetCloneFolder_PathOverridesSet_IgnoresThem()
    {
        var config = Config(@"D:\Clones");
        var account = Account("https://github.com/askrinnik", @"E:\Work\Mine");
        config.Accounts.Add(account);
        account.Repositories.Add(new AccountRepositoryConfig { Name = "my-repo", Id = 1, Path = @"F:\Special\my-repo" });

        _resolver.GetCloneFolder(config, "askrinnik", "my-repo", null).ShouldBe(@"D:\Clones\askrinnik\my-repo");
        _resolver.GetWikiCloneFolder(config, "askrinnik", "my-repo", null).ShouldBe(@"D:\Clones\askrinnik\my-repo.wiki");
    }

    [Theory]
    [InlineData(@"D:\Work\MyRepo", @"D:\Work\MyRepo", @"D:\Work\MyRepo.wiki")]
    [InlineData(@"D:\Work\MyRepo\", @"D:\Work\MyRepo", @"D:\Work\MyRepo.wiki")]
    public void GetCloneFolder_ClonePathSet_ReturnsItAndWikiWithSuffix(
        string clonePath, string expectedClone, string expectedWiki)
    {
        var config = Config(@"D:\Clones");

        _resolver.GetCloneFolder(config, "askrinnik", "my-repo", clonePath).ShouldBe(expectedClone);
        _resolver.GetWikiCloneFolder(config, "askrinnik", "my-repo", clonePath).ShouldBe(expectedWiki);
    }

    [Theory]
    [InlineData("askrinnik", "..")]
    [InlineData("..", "my-repo")]
    [InlineData("askrinnik", @"C:\Windows")]
    [InlineData("askrinnik", @"a\b")]
    [InlineData("askrinnik", "CON")]
    [InlineData("askrinnik", "nul.txt")]
    [InlineData("askrinnik", "repo.")]
    [InlineData("askrinnik", "repo ")]
    [InlineData("askrinnik", "")]
    [InlineData("ask.rinnik", "my-repo")]
    [InlineData("", "my-repo")]
    public void GetCloneFolder_InvalidNames_Throws(string owner, string repository) =>
        Should.Throw<ArgumentException>(() => _resolver.GetCloneFolder(Config(), owner, repository, null));

    [Theory]
    [InlineData("askrinnik", @"..\..\x")]
    [InlineData("askrinnik", "CON")]
    [InlineData("..", "my-repo")]
    public void GetWikiCloneFolder_InvalidNames_Throws(string owner, string repository) =>
        Should.Throw<ArgumentException>(() => _resolver.GetWikiCloneFolder(Config(), owner, repository, null));

    /// <summary>Creates a configuration with <c>C:\Backups</c> as the backup root.</summary>
    /// <param name="sourcesRoot">The clone root, or <see langword="null"/> for the default.</param>
    /// <returns>The configuration.</returns>
    private static BackupConfig Config(string? sourcesRoot = null) =>
        new() { BackupRoot = @"C:\Backups", SourcesRoot = sourcesRoot };

    /// <summary>Creates an account.</summary>
    /// <param name="url">The account URL.</param>
    /// <param name="path">The archive folder of the account, or <see langword="null"/>.</param>
    /// <returns>The account.</returns>
    private static AccountConfig Account(string url, string? path) => new() { Url = url, Path = path };

    /// <summary>Creates a standalone repository.</summary>
    /// <param name="url">The repository URL.</param>
    /// <param name="path">The archive folder of the repository, or <see langword="null"/>.</param>
    /// <returns>The repository.</returns>
    private static RepositoryConfig Standalone(string url, string? path) => new() { Url = url, Id = 1, Path = path };
}
