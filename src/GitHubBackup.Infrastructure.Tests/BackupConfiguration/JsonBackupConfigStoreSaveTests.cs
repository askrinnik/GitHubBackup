using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Infrastructure.BackupConfiguration;
using static GitHubBackup.Infrastructure.Tests.BackupConfiguration.BackupConfigSamples;

namespace GitHubBackup.Infrastructure.Tests.BackupConfiguration;

/// <summary>
/// Tests <see cref="JsonBackupConfigStore.SaveAsync"/>.
/// </summary>
public sealed class JsonBackupConfigStoreSaveTests
{
    /// <summary>The content of the configuration file before a write in the failure tests.</summary>
    private const string _originalContent = "original content";

    [Fact]
    public async Task SaveAsync_FullConfig_MatchesSnapshot()
    {
        var fileSystem = CreateFileSystem();

        await CreateStore(fileSystem).SaveAsync(CreateFullConfig(), TestContext.Current.CancellationToken);

        await Verify(fileSystem.File.ReadAllText(ConfigPath), extension: "json");
    }

    [Fact]
    public async Task SaveAsync_AnyConfig_WritesUtf8WithoutBomAndWindowsLineBreaks()
    {
        var fileSystem = CreateFileSystem();

        await CreateStore(fileSystem).SaveAsync(CreateFullConfig(), TestContext.Current.CancellationToken);

        var bytes = fileSystem.File.ReadAllBytes(ConfigPath);
        bytes[0].ShouldBe((byte)'{');
        var text = Encoding.UTF8.GetString(bytes);
        text.ShouldEndWith("}\r\n");
        text.Replace("\r\n", "", StringComparison.Ordinal).ShouldNotContain('\n');
        text.ShouldContain("\r\n  \"schemaVersion\": 1,\r\n");
    }

    [Fact]
    public async Task SaveAsync_DocumentedSample_SavesSameConfiguration()
    {
        var fileSystem = CreateFileSystem(DocumentedSample);
        var store = CreateStore(fileSystem);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        await store.SaveAsync(loaded.Config, TestContext.Current.CancellationToken);

        var saved = fileSystem.File.ReadAllText(ConfigPath);
        Canonical(saved).ShouldBe(Canonical(DocumentedSample));
    }

    [Fact]
    public async Task SaveAsync_SavedFileLoadedAndSavedAgain_IsByteForByteIdentical()
    {
        var fileSystem = CreateFileSystem(DocumentedSample);
        var store = CreateStore(fileSystem);
        await store.SaveAsync((await store.LoadAsync(TestContext.Current.CancellationToken)).Config, TestContext.Current.CancellationToken);
        var firstSave = fileSystem.File.ReadAllBytes(ConfigPath);

        await store.SaveAsync((await store.LoadAsync(TestContext.Current.CancellationToken)).Config, TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllBytes(ConfigPath).ShouldBe(firstSave);
    }

    [Fact]
    public async Task SaveAsync_NoOriginalFile_CreatesFileWithoutBackup()
    {
        var fileSystem = CreateFileSystem();

        await CreateStore(fileSystem).SaveAsync(CreateFullConfig(), TestContext.Current.CancellationToken);

        fileSystem.File.Exists(ConfigPath).ShouldBeTrue();
        FilesInFolder(fileSystem).ShouldBe([ConfigPath]);
    }

    [Fact]
    public async Task SaveAsync_OriginalFileExists_MovesPreviousContentToBackup()
    {
        var fileSystem = CreateFileSystem(DocumentedSample);

        await CreateStore(fileSystem).SaveAsync(CreateFullConfig(), TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllText(BackupPath).ShouldBe(DocumentedSample);
        FilesInFolder(fileSystem).ShouldBe([ConfigPath, BackupPath]);
    }

    [Fact]
    public async Task SaveAsync_ThirdWrite_BackupHoldsSecondContent()
    {
        var fileSystem = CreateFileSystem();
        var store = CreateStore(fileSystem);
        var config = CreateFullConfig();
        await store.SaveAsync(config, TestContext.Current.CancellationToken);
        config.Repositories[0].Branch = "second";
        await store.SaveAsync(config, TestContext.Current.CancellationToken);
        var secondContent = fileSystem.File.ReadAllText(ConfigPath);
        config.Repositories[0].Branch = "third";

        await store.SaveAsync(config, TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllText(BackupPath).ShouldBe(secondContent);
        fileSystem.File.ReadAllText(ConfigPath).ShouldContain("\"branch\": \"third\"");
        FilesInFolder(fileSystem).ShouldBe([ConfigPath, BackupPath]);
    }

    [Fact]
    public async Task SaveAsync_InvalidConfig_ThrowsAndWritesNothing()
    {
        var fileSystem = CreateFileSystem(_originalContent);
        var config = CreateFullConfig();
        config.Accounts[0].Url = "https://example.com/askrinnik";

        var exception = await Should.ThrowAsync<BackupConfigException>(
            () => CreateStore(fileSystem).SaveAsync(config, TestContext.Current.CancellationToken));

        exception.Errors.ShouldBe(["accounts[0].url must be an account URL of the form https://github.com/<login>."]);
        fileSystem.File.ReadAllText(ConfigPath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([ConfigPath]);
    }

    [Fact]
    public async Task SaveAsync_WriteOfTemporaryFileFails_KeepsOriginalFileAndRemovesTemporaryFile()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailStreamWrite = true }, _originalContent);

        await Should.ThrowAsync<IOException>(
            () => CreateStore(fileSystem).SaveAsync(CreateFullConfig(), TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(ConfigPath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([ConfigPath]);
    }

    [Fact]
    public async Task SaveAsync_ReplaceFails_KeepsOriginalFileAndRemovesTemporaryFile()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailReplace = true }, _originalContent);

        await Should.ThrowAsync<IOException>(
            () => CreateStore(fileSystem).SaveAsync(CreateFullConfig(), TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(ConfigPath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([ConfigPath]);
    }

    [Fact]
    public async Task SaveAsync_ReplaceFailsAfterBackup_MovesNewContentToFinalName()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailReplaceAfterBackup = true }, _originalContent);

        await Should.ThrowAsync<IOException>(
            () => CreateStore(fileSystem).SaveAsync(CreateFullConfig(), TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(ConfigPath).ShouldContain("\"schemaVersion\": 1");
        fileSystem.File.ReadAllText(BackupPath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([ConfigPath, BackupPath]);
    }

    [Fact]
    public async Task SaveAsync_ReplaceFailsAfterBackupAndMoveFails_RestoresOriginalFile()
    {
        var fileSystem = Populate(
            new FaultInjectingFileSystem { FailReplaceAfterBackup = true, FailMove = true }, _originalContent);

        await Should.ThrowAsync<IOException>(
            () => CreateStore(fileSystem).SaveAsync(CreateFullConfig(), TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(ConfigPath).ShouldBe(_originalContent);
        fileSystem.File.ReadAllText(BackupPath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([ConfigPath, BackupPath]);
    }

    [Fact]
    public async Task SaveAsync_ReplaceAndCleanupFail_ThrowsWriteFailureAndKeepsOriginalFile()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailReplace = true, FailDelete = true }, _originalContent);

        var exception = await Should.ThrowAsync<IOException>(
            () => CreateStore(fileSystem).SaveAsync(CreateFullConfig(), TestContext.Current.CancellationToken));

        exception.Message.ShouldBe("Injected failure of File.Replace.");
        fileSystem.File.ReadAllText(ConfigPath).ShouldBe(_originalContent);
    }

    [Fact]
    public async Task SaveAsync_Cancelled_KeepsOriginalFileAndRemovesTemporaryFile()
    {
        var fileSystem = CreateFileSystem(_originalContent);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateStore(fileSystem).SaveAsync(CreateFullConfig(), cancellation.Token));

        fileSystem.File.ReadAllText(ConfigPath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([ConfigPath]);
    }

    /// <summary>
    /// Creates a valid configuration that sets every property, with both statuses and non-ASCII paths.
    /// </summary>
    /// <returns>The configuration.</returns>
    private static BackupConfig CreateFullConfig() => new()
    {
        SchemaVersion = 1,
        BackupRoot = @"D:\Backups\GitHub",
        SourcesRoot = @"E:\Клоны",
        Exclude = ["someone/*", "*-archive"],
        Accounts =
        [
            new()
            {
                Url = "https://github.com/askrinnik",
                Path = null,
                Exclude = ["test-*"],
                Repositories =
                [
                    new() { Name = "my-repo", Id = 123456789, Branch = null, Path = null, Status = RepositoryStatus.Active },
                    new() { Name = "old-tool", Id = 223456789, Branch = "legacy", Path = @"F:\Архив\old-tool", Status = RepositoryStatus.Unavailable },
                ],
            },
            new() { Url = "https://github.com/AMTOSS", Path = @"D:\Work\AMTOSS" },
        ],
        Repositories =
        [
            new() { Url = "https://github.com/someone-else/library", Id = 323456789, Branch = "develop", Path = null },
        ],
    };

    /// <summary>
    /// Returns <paramref name="json"/> without whitespace outside string values, to compare documents by content.
    /// </summary>
    /// <param name="json">A JSON document.</param>
    /// <returns>The document without insignificant whitespace.</returns>
    private static string Canonical(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    /// <summary>
    /// Returns the files in the folder of the configuration file, in ordinal order.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <returns>The full paths of the files.</returns>
    private static string[] FilesInFolder(MockFileSystem fileSystem) =>
        [.. fileSystem.Directory.GetFiles(ContentRootPath).Order(StringComparer.Ordinal)];
}
