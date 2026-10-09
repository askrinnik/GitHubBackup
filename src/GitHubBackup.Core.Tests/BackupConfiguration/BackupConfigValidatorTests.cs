using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Tests.BackupConfiguration;

/// <summary>
/// Tests <see cref="BackupConfigValidator"/>.
/// </summary>
public sealed class BackupConfigValidatorTests
{
    /// <summary>The clone folder used when the configuration does not set one.</summary>
    private const string _defaultSourcesRoot = @"C:\app\sources";

    private readonly BackupConfigValidator _validator = new();

    [Fact]
    public void Validate_DocumentedSample_IsValidWithoutWarnings()
    {
        var result = Validate(CreateSample());

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_EmptyConfigurationWithBackupRoot_IsValid() =>
        Validate(new() { BackupRoot = @"C:\Backups" }).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Validate_UnsupportedSchemaVersion_ReturnsOnlyThatError(int schemaVersion)
    {
        var config = CreateSample();
        config.SchemaVersion = schemaVersion;
        config.BackupRoot = "relative";

        var result = Validate(config);

        result.Errors.ShouldBe([$"schemaVersion {schemaVersion} is not supported; the supported version is 1."]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BackupRootNotSet_ReturnsError(string backupRoot)
    {
        var config = CreateSample();
        config.BackupRoot = backupRoot;

        Validate(config).Errors.ShouldBe(["backupRoot must be set."]);
    }

    [Theory]
    [InlineData("Backups")]
    [InlineData(@"Backups\GitHub")]
    [InlineData(@".\Backups")]
    [InlineData(@"..\Backups")]
    [InlineData(@"C:Backups")]
    [InlineData(@"\Backups")]
    [InlineData(@"C:\Back|ups")]
    [InlineData(@"C:\Back?ups")]
    [InlineData(@"C:\Back*ups")]
    [InlineData(@"C:\Back<ups>")]
    [InlineData("C:\\Back\"ups")]
    [InlineData("C:\\Back\tups")]
    [InlineData(@"C:\Backups\a:b")]
    [InlineData(@"\\.\C:\Backups")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\C:\Backups")]
    [InlineData(@"//./C:/Backups")]
    [InlineData(@"\\?\UNC\server\share")]
    public void Validate_BackupRootNotAbsolute_ReturnsError(string backupRoot)
    {
        var config = CreateSample();
        config.BackupRoot = backupRoot;

        Validate(config).Errors.ShouldBe(["backupRoot must be an absolute path."]);
    }

    [Theory]
    [InlineData(@"C:\Backups")]
    [InlineData(@"C:\Backups\")]
    [InlineData(@"C:/Backups")]
    [InlineData(@"\\server\share\Backups")]
    [InlineData(@"D:\Резервные копии")]
    public void Validate_AbsoluteBackupRoot_IsValid(string backupRoot)
    {
        var config = CreateSample();
        config.BackupRoot = backupRoot;

        Validate(config).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("sources")]
    [InlineData(@"C:sources")]
    public void Validate_SourcesRootNotAbsolute_ReturnsError(string sourcesRoot)
    {
        var config = CreateSample();
        config.SourcesRoot = sourcesRoot;

        Validate(config).Errors.ShouldBe(["sourcesRoot must be an absolute path."]);
    }

    [Fact]
    public void Validate_AbsoluteSourcesRoot_IsValid()
    {
        var config = CreateSample();
        config.SourcesRoot = @"D:\Sources";

        Validate(config).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_NullExcludeMask_ReturnsErrorWithIndex()
    {
        var config = CreateSample();
        config.Exclude.Add(null!);
        config.Accounts[0].Exclude.Insert(0, null!);

        Validate(config).Errors.ShouldBe(["exclude[1] must not be null.", "accounts[0].exclude[0] must not be null."]);
    }

    [Fact]
    public void Validate_NullEntries_ReturnsErrorPerEntry()
    {
        var config = CreateSample();
        config.Accounts.Add(null!);
        config.Accounts[0].Repositories.Add(null!);
        config.Repositories.Add(null!);

        Validate(config).Errors.ShouldBe(
        [
            "accounts[0].repositories[2] must not be null.",
            "accounts[3] must not be null.",
            "repositories[1] must not be null.",
        ]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("askrinnik")]
    [InlineData("http://github.com/askrinnik")]
    [InlineData("https://github.com/askrinnik/my-repo")]
    [InlineData("https://example.com/askrinnik")]
    public void Validate_InvalidAccountUrl_ReturnsErrorNamingLocation(string url)
    {
        var config = CreateSample();
        config.Accounts[1].Url = url;

        Validate(config).Errors.ShouldBe(["accounts[1].url must be an account URL of the form https://github.com/<login>."]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://github.com/someone-else")]
    [InlineData("https://github.com/someone-else/library.git")]
    [InlineData("git@github.com:someone-else/library.git")]
    public void Validate_InvalidRepositoryUrl_ReturnsErrorNamingLocation(string url)
    {
        var config = CreateSample();
        config.Repositories[0].Url = url;

        Validate(config).Errors.ShouldBe(["repositories[0].url must be a repository URL of the form https://github.com/<owner>/<repo>."]);
    }

    [Theory]
    [InlineData("https://ghp_SecretToken0123456789@github.com/askrinnik")]
    [InlineData("https://x-access-token:ghp_SecretToken0123456789@github.com/someone/library")]
    [InlineData("https://github.com/askrinnik?access_token=ghp_SecretToken0123456789")]
    public void Validate_UrlWithToken_NeverQuotesUrl(string url)
    {
        var config = CreateSample();
        config.Accounts[0].Url = url;
        config.Repositories[0].Url = url;

        var result = Validate(config);

        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldAllBe(error => !error.Contains("ghp_SecretToken0123456789"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"..\escape")]
    [InlineData("my repo")]
    [InlineData("repo.git")]
    [InlineData("repo.")]
    [InlineData("NUL")]
    [InlineData("com1.backup")]
    public void Validate_InvalidRepositoryName_ReturnsError(string name)
    {
        var config = CreateSample();
        config.Accounts[0].Repositories[0].Name = name;

        Validate(config).Errors.ShouldBe(
            ["accounts[0].repositories[0].name must be a repository name of letters, digits, '.', '_' and '-'."]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_IdNotPositive_ReturnsErrorForBothKinds(long id)
    {
        var config = CreateSample();
        config.Accounts[0].Repositories[1].Id = id;
        config.Repositories[0].Id = id;

        Validate(config).Errors.ShouldBe(
        [
            "accounts[0].repositories[1].id must be a positive number.",
            "repositories[0].id must be a positive number.",
        ]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-x")]
    [InlineData("--upload-pack=touch /tmp/pwned")]
    public void Validate_InvalidBranch_ReturnsErrorForBothKinds(string branch)
    {
        var config = CreateSample();
        config.Accounts[0].Repositories[0].Branch = branch;
        config.Repositories[0].Branch = branch;

        Validate(config).Errors.ShouldBe(
        [
            "accounts[0].repositories[0].branch must be null or a branch name that is not empty and does not start with '-'.",
            "repositories[0].branch must be null or a branch name that is not empty and does not start with '-'.",
        ]);
    }

    [Theory]
    [InlineData("feature/x-1")]
    [InlineData("release-2.0")]
    public void Validate_ValidBranch_IsValid(string branch)
    {
        var config = CreateSample();
        config.Repositories[0].Branch = branch;

        Validate(config).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RelativePaths_ReturnsErrorPerLocation()
    {
        var config = CreateSample();
        config.Accounts[1].Path = "WorkBackups";
        config.Accounts[0].Repositories[1].Path = @"Archive\old-tool";
        config.Repositories[0].Path = "";

        Validate(config).Errors.ShouldBe(
        [
            "accounts[0].repositories[1].path must be an absolute path.",
            "accounts[1].path must be an absolute path.",
            "repositories[0].path must be an absolute path.",
        ]);
    }

    [Fact]
    public void Validate_DuplicateAccountIgnoringCase_ReturnsError()
    {
        var config = CreateSample();
        config.Accounts[2].Url = "https://github.com/AsKrinnik/";

        Validate(config).Errors.ShouldBe(["accounts[2].url names the same account as accounts[0].url."]);
    }

    [Fact]
    public void Validate_DuplicateIdAcrossKinds_ReturnsError()
    {
        var config = CreateSample();
        config.Repositories[0].Id = config.Accounts[0].Repositories[0].Id;

        Validate(config).Errors.ShouldBe(["repositories[0].id is the same as accounts[0].repositories[0].id."]);
    }

    [Fact]
    public void Validate_DuplicateNameInAccountIgnoringCase_ReturnsError()
    {
        var config = CreateSample();
        config.Accounts[0].Repositories[1].Name = "My-Repo";
        config.Accounts[0].Repositories[1].Path = null;

        var result = Validate(config);

        result.Errors.ShouldContain("accounts[0].repositories[1].name names the same repository as accounts[0].repositories[0].name.");
    }

    [Fact]
    public void Validate_StandaloneRepositoryOfTrackedAccountRepository_ReturnsDuplicateNameError()
    {
        var config = CreateSample();
        config.Repositories[0].Url = "https://github.com/ASKRINNIK/old-tool";

        Validate(config).Errors.ShouldBe(["repositories[0].url names the same repository as accounts[0].repositories[1].name."]);
    }

    [Fact]
    public void Validate_DuplicateStandaloneRepository_ReturnsDuplicateNameAndFolderErrors()
    {
        var config = CreateSample();
        config.Repositories.Add(new() { Url = "https://github.com/someone-else/library/", Id = 423456789 });

        Validate(config).Errors.ShouldBe(
        [
            "repositories[1].url names the same repository as repositories[0].url.",
            @"repositories[1] has the same archive folder 'C:\Users\Sanya\OneDrive\GitHubBackups\someone-else\library' as repositories[0].",
        ]);
    }

    [Fact]
    public void Validate_RepositoryPathsDifferOnlyInCaseAndTrailingSeparator_ReturnsFolderError()
    {
        var config = CreateSample();
        config.Repositories[0].Path = @"e:\archive\OLD-TOOL\";

        Validate(config).Errors.ShouldBe(
            [@"repositories[0] has the same archive folder 'e:\archive\OLD-TOOL' as accounts[0].repositories[1]."]);
    }

    [Fact]
    public void Validate_RepositoryPathEqualToDefaultFolderOfAnother_ReturnsFolderError()
    {
        var config = CreateSample();
        config.Repositories[0].Path = @"C:\Users\Sanya\OneDrive\GitHubBackups\askrinnik\my-repo";

        Validate(config).Errors.ShouldBe(
            [@"repositories[0] has the same archive folder 'C:\Users\Sanya\OneDrive\GitHubBackups\askrinnik\my-repo' as accounts[0].repositories[0]."]);
    }

    [Fact]
    public void Validate_TwoAccountsSharingPathWithSameRepositoryName_ReturnsFolderError()
    {
        var config = CreateSample();
        config.Accounts[2].Path = config.Accounts[1].Path;
        config.Accounts[1].Repositories.Add(new() { Name = "tools", Id = 1 });
        config.Accounts[2].Repositories.Add(new() { Name = "tools", Id = 2 });

        Validate(config).Errors.ShouldBe(
            [@"accounts[2].repositories[0] has the same archive folder 'E:\WorkBackups\AMTOSS\tools' as accounts[1].repositories[0]."]);
    }

    [Fact]
    public void Validate_EntryWithInvalidPath_IsLeftOutOfFolderCheck()
    {
        var config = CreateSample();
        config.Repositories[0].Path = @"C:\Users\Sanya\OneDrive\GitHubBackups\askrinnik\my-repo|";

        Validate(config).Errors.ShouldBe(["repositories[0].path must be an absolute path."]);
    }

    [Fact]
    public void Validate_SeveralProblems_ReturnsEveryError()
    {
        var config = CreateSample();
        config.BackupRoot = "relative";
        config.Accounts[0].Url = "https://github.com/";
        config.Accounts[0].Repositories[0].Id = 0;
        config.Repositories[0].Branch = "-b";

        Validate(config).Errors.Count.ShouldBe(4);
    }

    [Theory]
    [InlineData(@"C:\Users\Sanya\OneDrive\GitHubBackups")]
    [InlineData(@"C:\Users\Sanya\OneDrive\GitHubBackups\")]
    [InlineData(@"c:\users\sanya\onedrive\githubbackups\.sources")]
    public void Validate_SourcesRootInsideBackupRoot_ReturnsWarning(string sourcesRoot)
    {
        var config = CreateSample();
        config.SourcesRoot = sourcesRoot;

        var result = Validate(config);

        result.IsValid.ShouldBeTrue();
        result.Warnings.ShouldBe(
            [$"sourcesRoot '{sourcesRoot}' is inside backupRoot; the clones would be synchronised together with the archives."]);
    }

    [Fact]
    public void Validate_SourcesRootInsideArchiveFolder_ReturnsWarningNamingRepository()
    {
        var config = CreateSample();
        config.SourcesRoot = @"E:\Archive\old-tool\clones";

        Validate(config).Warnings.ShouldBe(
        [
            @"sourcesRoot 'E:\Archive\old-tool\clones' is inside the archive folder of accounts[0].repositories[1]; the clones would be synchronised together with the archives.",
        ]);
    }

    [Fact]
    public void Validate_DefaultSourcesRootInsideBackupRoot_ReturnsWarning()
    {
        var config = CreateSample();
        config.BackupRoot = @"C:\app";

        var result = _validator.Validate(config, _defaultSourcesRoot);

        result.Warnings.ShouldHaveSingleItem().ShouldStartWith(@"sourcesRoot 'C:\app\sources' is inside backupRoot");
    }

    [Theory]
    [InlineData(@"C:\Users\Sanya\OneDrive")]
    [InlineData(@"C:\Users\Sanya\OneDrive\GitHubBackupsOld")]
    [InlineData(@"E:\Archive")]
    public void Validate_SourcesRootOutsideArchiveTree_ReturnsNoWarning(string sourcesRoot)
    {
        var config = CreateSample();
        config.SourcesRoot = sourcesRoot;

        Validate(config).Warnings.ShouldBeEmpty();
    }

    /// <summary>
    /// Creates the configuration of the documented <c>backup-config.json</c> sample.
    /// </summary>
    /// <returns>A new, valid configuration.</returns>
    internal static BackupConfig CreateSample() => new()
    {
        SchemaVersion = 1,
        BackupRoot = @"C:\Users\Sanya\OneDrive\GitHubBackups",
        SourcesRoot = null,
        Exclude = ["someone/*"],
        Accounts =
        [
            new()
            {
                Url = "https://github.com/askrinnik",
                Exclude = ["test-*"],
                Repositories =
                [
                    new() { Name = "my-repo", Id = 123456789 },
                    new() { Name = "old-tool", Id = 223456789, Branch = "legacy", Path = @"E:\Archive\old-tool" },
                ],
            },
            new() { Url = "https://github.com/AMTOSS", Path = @"E:\WorkBackups\AMTOSS" },
            new() { Url = "https://github.com/get-the-manual" },
        ],
        Repositories =
        [
            new() { Url = "https://github.com/someone-else/library", Id = 323456789, Branch = "develop" },
        ],
    };

    /// <summary>
    /// Validates <paramref name="config"/> with a default clone folder outside the archive tree.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <returns>The validation result.</returns>
    private BackupConfigValidationResult Validate(BackupConfig config) => _validator.Validate(config, _defaultSourcesRoot);
}
