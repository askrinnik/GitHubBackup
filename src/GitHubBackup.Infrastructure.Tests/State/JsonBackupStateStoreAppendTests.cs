using System.IO.Abstractions.TestingHelpers;
using System.Text;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.State;
using GitHubBackup.Infrastructure.State;
using GitHubBackup.Infrastructure.Tests.BackupConfiguration;
using static GitHubBackup.Infrastructure.Tests.State.BackupStateSamples;

namespace GitHubBackup.Infrastructure.Tests.State;

/// <summary>
/// Tests <see cref="JsonBackupStateStore.AppendAsync"/>.
/// </summary>
public sealed class JsonBackupStateStoreAppendTests
{
    /// <summary>The content of the state file before a write in the failure tests.</summary>
    private const string _originalContent = "original content";

    /// <summary>The file name of the archive recorded by the failure tests.</summary>
    private const string _archiveFile = "new.zip";

    [Fact]
    public async Task AppendAsync_DocumentedSample_WritesExpectedJson()
    {
        var fileSystem = Populate(new MockFileSystem(), null, SampleArchiveFile);
        var expected = """
            {
              "schemaVersion": 1,
              "repositoryId": 123456789,
              "fullName": "askrinnik/my-repo",
              "repository": {
                "archives": [
                  {
                    "file": "askrinnik_my-repo_20261002-143015_a1b2c3d4e5.zip",
                    "createdAt": "2026-10-02T14:30:15+03:00",
                    "branch": "main",
                    "headCommit": "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
                    "sizeBytes": 10485760,
                    "refs": {
                      "heads/dev": "77f0e1d2c3b4a5968778695a4b3c2d1e0f9a8b7c",
                      "heads/main": "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
                      "tags/v1.0": "c3d4e5f60718293a4b5c6d7e8f9012345678a1b2"
                    }
                  }
                ]
              },
              "wiki": { "archives": [] }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n";

        await CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateSampleRecord(), TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllText(StatePath).ShouldBe(expected);
    }

    [Fact]
    public async Task AppendAsync_AnyState_WritesUtf8WithoutBomAndWindowsLineBreaks()
    {
        var fileSystem = Populate(new MockFileSystem(), null, SampleArchiveFile);

        await CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateSampleRecord(), TestContext.Current.CancellationToken);

        var bytes = fileSystem.File.ReadAllBytes(StatePath);
        bytes[0].ShouldBe((byte)'{');
        var text = Encoding.UTF8.GetString(bytes);
        text.ShouldEndWith("}\r\n");
        text.Replace("\r\n", "", StringComparison.Ordinal).ShouldNotContain('\n');
        text.ShouldContain("\r\n  \"schemaVersion\": 1,\r\n");
    }

    [Fact]
    public async Task AppendAsync_NonAsciiFullName_WritesCharactersUnescaped()
    {
        var fileSystem = Populate(new MockFileSystem(), null, _archiveFile);

        await CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, "владелец/репо", BackupTarget.Repository, CreateRecord(_archiveFile), TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllText(StatePath).ShouldContain("\"fullName\": \"владелец/репо\"");
    }

    [Fact]
    public async Task AppendAsync_NoStateFile_CreatesFileWithoutBackup()
    {
        var fileSystem = Populate(new MockFileSystem(), null, _archiveFile);

        var state = await CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateRecord(_archiveFile), TestContext.Current.CancellationToken);

        state.Repository.Archives.ShouldBe([CreateRecord(_archiveFile)]);
        FilesInFolder(fileSystem).ShouldBe([StatePath, Path.Combine(ArchiveFolder, _archiveFile)]);
    }

    [Fact]
    public async Task AppendAsync_ExistingFile_AppendsAndKeepsPreviousAsBackup()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample, SampleArchiveFile, _archiveFile);
        var store = CreateStore(fileSystem);

        await store.AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateRecord(_archiveFile), TestContext.Current.CancellationToken);

        var loaded = await store.LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken);
        loaded.Repository.Archives.ShouldBe([CreateSampleRecord(), CreateRecord(_archiveFile)]);
        loaded.GetLastArchive(BackupTarget.Repository).ShouldBe(CreateRecord(_archiveFile));
        fileSystem.File.ReadAllText(BackupPath).ShouldBe(DocumentedSample);
    }

    [Fact]
    public async Task AppendAsync_Wiki_LeavesRepositorySectionUnchanged()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample, SampleArchiveFile, _archiveFile);
        var store = CreateStore(fileSystem);

        await store.AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Wiki, CreateRecord(_archiveFile), TestContext.Current.CancellationToken);

        var loaded = await store.LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken);
        loaded.Repository.Archives.ShouldBe([CreateSampleRecord()]);
        loaded.Wiki.Archives.ShouldBe([CreateRecord(_archiveFile)]);
    }

    [Fact]
    public async Task AppendAsync_RenamedRepository_WritesNewFullName()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample, SampleArchiveFile, _archiveFile);

        var state = await CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, "askrinnik/renamed", BackupTarget.Repository, CreateRecord(_archiveFile), TestContext.Current.CancellationToken);

        state.FullName.ShouldBe("askrinnik/renamed");
        fileSystem.File.ReadAllText(StatePath).ShouldContain("\"fullName\": \"askrinnik/renamed\"");
    }

    [Fact]
    public async Task AppendAsync_OtherRepositoryId_ThrowsAndLeavesFileUnchanged()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample, SampleArchiveFile, _archiveFile);

        var exception = await Should.ThrowAsync<BackupStateException>(() => CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId + 1, FullName, BackupTarget.Repository, CreateRecord(_archiveFile), TestContext.Current.CancellationToken));

        exception.StatePath.ShouldBe(StatePath);
        fileSystem.File.ReadAllText(StatePath).ShouldBe(DocumentedSample);
        fileSystem.File.Exists(BackupPath).ShouldBeFalse();
    }

    [Fact]
    public async Task AppendAsync_ArchiveFileMissing_ThrowsAndWritesNothing()
    {
        var fileSystem = Populate(new MockFileSystem(), _originalContent);

        var exception = await Should.ThrowAsync<BackupStateException>(() => CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateRecord(_archiveFile), TestContext.Current.CancellationToken));

        exception.StatePath.ShouldBe(StatePath);
        exception.Errors.ShouldHaveSingleItem().ShouldContain(_archiveFile);
        fileSystem.File.ReadAllText(StatePath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([StatePath]);
    }

    [Fact]
    public async Task AppendAsync_WriteOfTemporaryFileFails_KeepsOriginalFileAndRemovesTemporaryFile()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailStreamWrite = true }, DocumentedSample, _archiveFile);

        await Should.ThrowAsync<IOException>(() => CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateRecord(_archiveFile), TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(StatePath).ShouldBe(DocumentedSample);
        FilesInFolder(fileSystem).ShouldBe([StatePath, Path.Combine(ArchiveFolder, _archiveFile)]);
    }

    [Fact]
    public async Task AppendAsync_ReplaceFails_KeepsOriginalFile()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailReplace = true }, DocumentedSample, _archiveFile);

        await Should.ThrowAsync<IOException>(() => CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateRecord(_archiveFile), TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(StatePath).ShouldBe(DocumentedSample);
        FilesInFolder(fileSystem).ShouldBe([StatePath, Path.Combine(ArchiveFolder, _archiveFile)]);
    }

    [Fact]
    public async Task AppendAsync_Cancelled_KeepsOriginalFileAndRemovesTemporaryFile()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample, _archiveFile);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateRecord(_archiveFile), cancellation.Token));

        fileSystem.File.ReadAllText(StatePath).ShouldBe(DocumentedSample);
        FilesInFolder(fileSystem).ShouldBe([StatePath, Path.Combine(ArchiveFolder, _archiveFile)]);
    }

    [Fact]
    public async Task AppendAsync_UtcRecord_WritesZeroOffset()
    {
        var fileSystem = Populate(new MockFileSystem(), null, _archiveFile);
        var record = CreateRecordCreatedAt(new(2026, 10, 2, 11, 30, 15, TimeSpan.Zero));

        await CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, record, TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllText(StatePath).ShouldContain("\"createdAt\": \"2026-10-02T11:30:15+00:00\"");
    }

    [Fact]
    public async Task AppendAsync_FractionalSeconds_WritesWholeSeconds()
    {
        var fileSystem = Populate(new MockFileSystem(), null, _archiveFile);
        var createdAt = new DateTimeOffset(2026, 10, 2, 14, 30, 15, TimeSpan.FromHours(3)).AddTicks(9_999_999);

        await CreateStore(fileSystem).AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateRecordCreatedAt(createdAt), TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllText(StatePath).ShouldContain("\"createdAt\": \"2026-10-02T14:30:15+03:00\"");
    }

    /// <summary>
    /// Creates a valid record of <see cref="_archiveFile"/> created at <paramref name="createdAt"/>.
    /// </summary>
    /// <param name="createdAt">The creation time.</param>
    /// <returns>The record.</returns>
    private static ArchiveRecord CreateRecordCreatedAt(DateTimeOffset createdAt) =>
        new(_archiveFile, createdAt, "main", "0123456789abcdef0123456789abcdef01234567", 1, RefSnapshot.Empty);
}
