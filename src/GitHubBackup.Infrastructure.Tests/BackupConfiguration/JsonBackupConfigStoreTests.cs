using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Infrastructure.BackupConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static GitHubBackup.Infrastructure.Tests.BackupConfiguration.BackupConfigSamples;

namespace GitHubBackup.Infrastructure.Tests.BackupConfiguration;

/// <summary>
/// Tests <see cref="JsonBackupConfigStore.LoadAsync"/> and <see cref="JsonBackupConfigStore.Validate"/>.
/// </summary>
public sealed class JsonBackupConfigStoreTests
{
    /// <summary>A token value that must never reach an error message.</summary>
    private const string _token = "ghp_TestTokenValue0123456789abcdefABCDEF";

    private const int _validationWarningEventId = 1;

    [Fact]
    public async Task LoadAsync_DocumentedSample_ReadsEveryField()
    {
        var result = await LoadAsync(DocumentedSample);

        result.Warnings.ShouldBeEmpty();
        var config = result.Config;
        config.SchemaVersion.ShouldBe(1);
        config.BackupRoot.ShouldBe(@"C:\Users\Sanya\OneDrive\GitHubBackups");
        config.SourcesRoot.ShouldBeNull();
        config.Exclude.ShouldBe(["someone/*"]);
        config.DiscoverAccessibleRepositories.ShouldBeFalse();
        config.Accounts.Select(account => account.Url).ShouldBe(
            ["https://github.com/askrinnik", "https://github.com/AMTOSS", "https://github.com/get-the-manual"]);
        config.Accounts[0].Exclude.ShouldBe(["test-*"]);
        config.Accounts[1].Path.ShouldBe(@"E:\WorkBackups\AMTOSS");
        var oldTool = config.Accounts[0].Repositories[1];
        oldTool.Name.ShouldBe("old-tool");
        oldTool.Id.ShouldBe(223456789);
        oldTool.Branch.ShouldBe("legacy");
        oldTool.Path.ShouldBe(@"E:\Archive\old-tool");
        oldTool.Status.ShouldBe(RepositoryStatus.Active);
        var library = config.Repositories.ShouldHaveSingleItem();
        library.Url.ShouldBe("https://github.com/someone-else/library");
        library.Id.ShouldBe(323456789);
        library.Branch.ShouldBe("develop");
        library.Path.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_DiscoverAccessibleRepositoriesTrue_ReadsTrue()
    {
        var result = await LoadAsync(
            """{ "schemaVersion": 1, "backupRoot": "D:\\B", "discoverAccessibleRepositories": true }""");

        result.Config.DiscoverAccessibleRepositories.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_FileWithByteOrderMark_ReadsFile()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.AddFile(ConfigPath, new MockFileData([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(DocumentedSample)]));

        var result = await CreateStore(fileSystem).LoadAsync(TestContext.Current.CancellationToken);

        result.Config.Accounts.Count.ShouldBe(3);
    }

    [Fact]
    public async Task LoadAsync_ListsAndOptionalValuesMissing_UsesDefaults()
    {
        var result = await LoadAsync(
            """
            {
              "schemaVersion": 1,
              "backupRoot": "D:\\Backups",
              "accounts": [ { "url": "https://github.com/askrinnik", "repositories": [ { "name": "my-repo", "id": 1 } ] } ]
            }
            """);

        var config = result.Config;
        config.SourcesRoot.ShouldBeNull();
        config.Exclude.ShouldBeEmpty();
        config.DiscoverAccessibleRepositories.ShouldBeFalse();
        config.Repositories.ShouldBeEmpty();
        config.Accounts[0].Exclude.ShouldBeEmpty();
        var repository = config.Accounts[0].Repositories.ShouldHaveSingleItem();
        repository.Branch.ShouldBeNull();
        repository.Path.ShouldBeNull();
        repository.Status.ShouldBe(RepositoryStatus.Active);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""
        "id": null,
        """)]
    [InlineData("""
        "id": 0,
        """)]
    public async Task LoadAsync_IdMissingNullOrZero_ReadsZeroForBothKinds(string id)
    {
        var result = await LoadAsync(
            $$"""
            {
              "schemaVersion": 1,
              "backupRoot": "D:\\Backups",
              "accounts": [ { "url": "https://github.com/askrinnik", "repositories": [ { {{id}}"name": "my-repo" } ] } ],
              "repositories": [ { {{id}}"url": "https://github.com/o/r" } ]
            }
            """);

        result.Config.Accounts[0].Repositories.ShouldHaveSingleItem().Id.ShouldBe(0);
        result.Config.Repositories.ShouldHaveSingleItem().Id.ShouldBe(0);
    }

    [Fact]
    public async Task LoadAsync_ClonePath_ReadsItForBothKinds()
    {
        var result = await LoadAsync(
            """
            {
              "schemaVersion": 1,
              "backupRoot": "D:\\Backups",
              "accounts": [ { "url": "https://github.com/askrinnik", "repositories": [ { "name": "my-repo", "id": 1, "clonePath": "E:\\Work\\MyRepo" } ] } ],
              "repositories": [ { "url": "https://github.com/o/r", "id": 2, "clonePath": null } ]
            }
            """);

        result.Config.Accounts[0].Repositories.ShouldHaveSingleItem().ClonePath.ShouldBe(@"E:\Work\MyRepo");
        result.Config.Repositories.ShouldHaveSingleItem().ClonePath.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_RelativeClonePath_ThrowsValidationError()
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(() => LoadAsync(
            """
            {
              "schemaVersion": 1,
              "backupRoot": "D:\\Backups",
              "repositories": [ { "url": "https://github.com/o/r", "id": 2, "clonePath": "Work\\MyRepo" } ]
            }
            """));

        exception.Errors.ShouldBe(["repositories[0].clonePath must be an absolute path."]);
    }

    [Theory]
    [InlineData("""{ "name": "my-repo", "id": -5 }""", "[]", "accounts[0].repositories[0].id must be a positive number when set.")]
    [InlineData("", """[ { "url": "https://github.com/o/r", "id": -5 } ]""", "repositories[0].id must be a positive number when set.")]
    public async Task LoadAsync_NegativeId_ThrowsValidationError(string accountRepository, string repositories, string error)
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(() => LoadAsync(
            $$"""
            {
              "schemaVersion": 1,
              "backupRoot": "D:\\Backups",
              "accounts": [ { "url": "https://github.com/askrinnik", "repositories": [ {{accountRepository}} ] } ],
              "repositories": {{repositories}}
            }
            """));

        exception.Errors.ShouldBe([error]);
    }

    [Fact]
    public async Task LoadAsync_IdOfWrongType_ThrowsNamingJsonPath()
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(
            () => LoadAsync(WithStandaloneRepository("""{ "url": "https://github.com/o/r", "id": "1" }""")));

        exception.Errors.ShouldHaveSingleItem().ShouldContain("(JSON path $.repositories[0].id)");
    }

    [Theory]
    [InlineData("Unavailable", RepositoryStatus.Unavailable)]
    [InlineData("unavailable", RepositoryStatus.Unavailable)]
    [InlineData("ACTIVE", RepositoryStatus.Active)]
    public async Task LoadAsync_StatusInAnyCase_ReadsStatus(string status, RepositoryStatus expected)
    {
        var result = await LoadAsync(WithStandaloneRepository($$"""{ "url": "https://github.com/o/r", "id": 1, "status": "{{status}}" }"""));

        result.Config.Repositories.ShouldHaveSingleItem().Status.ShouldBe(expected);
    }

    [Fact]
    public async Task LoadAsync_CommentsAndTrailingCommas_ReadsFile()
    {
        var result = await LoadAsync(
            """
            {
              // Edited by hand.
              "schemaVersion": 1,
              "backupRoot": "D:\\Backups", /* archives */
              "exclude": [ "a/*", ],
            }
            """);

        result.Config.Exclude.ShouldBe(["a/*"]);
    }

    [Fact]
    public async Task LoadAsync_FileMissing_ThrowsNotFoundWithHint()
    {
        var exception = await Should.ThrowAsync<BackupConfigNotFoundException>(
            () => CreateStore(CreateFileSystem()).LoadAsync(TestContext.Current.CancellationToken));

        exception.ConfigPath.ShouldBe(ConfigPath);
        exception.Errors.ShouldBe(
            [$"The backup configuration file '{ConfigPath}' does not exist. Create it in the settings editor of GitHubBackup.exe."]);
        exception.Message.ShouldContain(ConfigPath);
    }

    [Fact]
    public async Task LoadAsync_FolderMissing_ThrowsNotFound()
    {
        var fileSystem = CreateFailingFileSystem(new DirectoryNotFoundException("Could not find a part of the path."));

        var exception = await Should.ThrowAsync<BackupConfigNotFoundException>(
            () => CreateStore(fileSystem).LoadAsync(TestContext.Current.CancellationToken));

        exception.Errors.ShouldBe([BackupConfigNotFoundException.FormatError(ConfigPath)]);
        exception.InnerException.ShouldBeOfType<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task LoadAsync_FileUnreadable_ThrowsBaseExceptionNotNotFound()
    {
        var fileSystem = CreateFailingFileSystem(new UnauthorizedAccessException("Access is denied."));

        var exception = await Should.ThrowAsync<BackupConfigException>(
            () => CreateStore(fileSystem).LoadAsync(TestContext.Current.CancellationToken));

        exception.ShouldNotBeOfType<BackupConfigNotFoundException>();
        exception.Errors.ShouldBe([$"The backup configuration file '{ConfigPath}' cannot be read: Access is denied."]);
    }

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("   ", 1, 4)]
    [InlineData("""{ "schemaVersion": 1, """, 1, 23)]
    [InlineData("{\n  \"schemaVersion\": 1\n  \"backupRoot\": \"D:\\\\B\"\n}", 3, 3)]
    [InlineData("[]", 1, 2)]
    public async Task LoadAsync_MalformedJson_ThrowsWithPosition(string json, int line, int position)
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(() => LoadAsync(json));

        exception.Errors.ShouldHaveSingleItem().ShouldStartWith(
            $"The backup configuration file '{ConfigPath}' is not valid at line {line}, position {position}");
        exception.InnerException.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "backupRoot": "D:\\B", "repositories": [ { "url": "https://github.com/o/r", "id": 1, "status": "Archived" } ] }""", "$.repositories[0].status")]
    [InlineData("""{ "schemaVersion": 1, "backupRoot": "D:\\B", "repositories": [ { "url": "https://github.com/o/r", "id": 1, "status": 1 } ] }""", "$.repositories[0].status")]
    [InlineData("""{ "schemaVersion": "1", "backupRoot": "D:\\B" }""", "$.schemaVersion")]
    [InlineData("""{ "schemaVersion": 1, "backupRoot": "D:\\B", "accounts": {} }""", "$.accounts")]
    [InlineData("""{ "schemaVersion": 1, "backupRoot": null }""", "$.backupRoot")]
    [InlineData("""{ "schemaVersion": 1, "backupRoot": "D:\\B", "exclude": null }""", "$.exclude")]
    [InlineData("""{ "schemaVersion": 1, "backupRoot": "D:\\B", "discoverAccessibleRepositories": null }""", "$.discoverAccessibleRepositories")]
    [InlineData("""{ "schemaVersion": 1, "backupRoot": "D:\\B", "discoverAccessibleRepositories": "yes" }""", "$.discoverAccessibleRepositories")]
    public async Task LoadAsync_ValueOfWrongTypeOrNull_ThrowsNamingJsonPath(string json, string jsonPath)
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(() => LoadAsync(json));

        exception.Errors.ShouldHaveSingleItem().ShouldContain($"(JSON path {jsonPath})");
    }

    [Fact]
    public async Task LoadAsync_UnknownProperty_Throws()
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(
            () => LoadAsync("""{ "schemaVersion": 1, "backupRoot": "D:\\B", "backupRot": "E:\\B" }"""));

        exception.Errors.ShouldHaveSingleItem().ShouldContain("is not valid at line 1");
    }

    [Fact]
    public async Task LoadAsync_NullDocument_Throws()
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(() => LoadAsync("null"));

        exception.Errors.ShouldBe([$"The backup configuration file '{ConfigPath}' must contain a JSON object, not null."]);
    }

    [Fact]
    public async Task LoadAsync_NullListEntry_ThrowsValidationError()
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(
            () => LoadAsync("""{ "schemaVersion": 1, "backupRoot": "D:\\B", "accounts": [ null ] }"""));

        exception.Errors.ShouldBe(["accounts[0] must not be null."]);
    }

    [Theory]
    [InlineData($$"""{ "schemaVersion": 1, "backupRoot": "D:\\B", "unknown": "{{_token}}" }""")]
    [InlineData($$"""{ "schemaVersion": 1, "backupRoot": "D:\\B", "repositories": [ { "url": "https://github.com/o/r", "id": 1, "status": "{{_token}}" } ] }""")]
    [InlineData($$"""{ "schemaVersion": 1, "backupRoot": "D:\\B", "exclude": [ "{{_token}} ] }""")]
    [InlineData($$"""{ "schemaVersion": 1, "backupRoot": "D:\\B", "accounts": [ { "url": "https://{{_token}}@github.com/o" } ] }""")]
    public async Task LoadAsync_InvalidFileWithToken_NeverQuotesToken(string json)
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(() => LoadAsync(json));

        exception.Message.ShouldNotContain(_token);
        exception.Errors.ShouldAllBe(error => !error.Contains(_token));
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_UnsupportedSchemaVersion_Throws()
    {
        var exception = await Should.ThrowAsync<BackupConfigException>(
            () => LoadAsync("""{ "schemaVersion": 2, "backupRoot": "D:\\B" }"""));

        exception.Errors.ShouldBe(["schemaVersion 2 is not supported; the supported version is 1."]);
    }

    [Fact]
    public async Task LoadAsync_ValidationErrors_ThrowsWithEveryError()
    {
        var json = DocumentedSample
            .Replace("https://github.com/AMTOSS", "https://gitlab.com/AMTOSS", StringComparison.Ordinal)
            .Replace("223456789", "123456789", StringComparison.Ordinal);

        var exception = await Should.ThrowAsync<BackupConfigException>(() => LoadAsync(json));

        exception.Errors.ShouldBe(
        [
            "accounts[0].repositories[1].id is the same as accounts[0].repositories[0].id.",
            "accounts[1].url must be an account URL of the form https://github.com/<login>.",
        ]);
        exception.Message.ShouldBe(
            $"The backup configuration '{ConfigPath}' is invalid: accounts[0].repositories[1].id is the same as accounts[0].repositories[0].id. (1 more errors)");
    }

    [Fact]
    public async Task LoadAsync_SourcesRootInsideBackupRoot_ReturnsAndLogsWarning()
    {
        var logger = new FakeLogger<JsonBackupConfigStore>();
        var json = DocumentedSample.Replace(
            "\"sourcesRoot\": null", "\"sourcesRoot\": \"C:\\\\Users\\\\Sanya\\\\OneDrive\\\\GitHubBackups\\\\src\"", StringComparison.Ordinal);
        var fileSystem = CreateFileSystem(json);

        var result = await CreateStore(fileSystem, logger).LoadAsync(TestContext.Current.CancellationToken);

        var warning = result.Warnings.ShouldHaveSingleItem();
        warning.ShouldContain("is inside backupRoot");
        var record = logger.Collector.GetSnapshot().Where(r => r.Id.Id == _validationWarningEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(ConfigPath);
        record.GetStructuredStateValue("Warning").ShouldBe(warning);
    }

    [Fact]
    public void Validate_InvalidConfig_ReturnsErrorsAndLeavesFileUnchanged()
    {
        var fileSystem = CreateFileSystem(DocumentedSample);
        var config = new BackupConfig { BackupRoot = "relative", Accounts = [new() { Url = "https://example.com/x" }] };

        var result = CreateStore(fileSystem).Validate(config);

        result.Errors.ShouldBe(
        [
            "backupRoot must be an absolute path.",
            "accounts[0].url must be an account URL of the form https://github.com/<login>.",
        ]);
        fileSystem.File.ReadAllText(ConfigPath).ShouldBe(DocumentedSample);
        fileSystem.File.Exists(BackupPath).ShouldBeFalse();
    }

    [Fact]
    public void Validate_NoSourcesRoot_ChecksDefaultCloneFolderOfContentRoot()
    {
        var config = new BackupConfig { BackupRoot = ContentRootPath };

        var result = CreateStore(CreateFileSystem()).Validate(config);

        result.IsValid.ShouldBeTrue();
        result.Warnings.ShouldHaveSingleItem().ShouldStartWith(@"sourcesRoot 'C:\app\sources' is inside backupRoot");
    }

    /// <summary>
    /// Returns a minimal valid configuration with one standalone repository.
    /// </summary>
    /// <param name="repositoryJson">The JSON object of the repository.</param>
    /// <returns>The configuration text.</returns>
    private static string WithStandaloneRepository(string repositoryJson) =>
        $$"""{ "schemaVersion": 1, "backupRoot": "D:\\Backups", "repositories": [ {{repositoryJson}} ] }""";

    /// <summary>
    /// Creates a file system whose attempt to open the configuration file throws <paramref name="exception"/>.
    /// </summary>
    /// <param name="exception">The exception opening the file throws.</param>
    /// <returns>The file system.</returns>
    private static IFileSystem CreateFailingFileSystem(Exception exception)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.File.OpenRead(ConfigPath).Throws(exception);

        return fileSystem;
    }

    /// <summary>
    /// Loads <paramref name="json"/> as the configuration file.
    /// </summary>
    /// <param name="json">The text of the configuration file.</param>
    /// <returns>The load result.</returns>
    private static Task<BackupConfigLoadResult> LoadAsync(string json) =>
        CreateStore(CreateFileSystem(json)).LoadAsync(TestContext.Current.CancellationToken);
}
