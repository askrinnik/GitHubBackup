using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.State;
using GitHubBackup.Infrastructure.Tests.BackupConfiguration;
using static GitHubBackup.Infrastructure.Tests.State.BackupStateSamples;

namespace GitHubBackup.Infrastructure.Tests.State;

/// <summary>
/// Tests <see cref="GitHubBackup.Infrastructure.State.JsonBackupStateStore.ReplaceAsync"/>.
/// </summary>
public sealed class JsonBackupStateStoreReplaceTests
{
    private const string _otherFile = "other.zip";

    [Fact]
    public async Task ReplaceAsync_ExistingFile_WritesStateAndKeepsPreviousAsBackup()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample, SampleArchiveFile);
        var store = CreateStore(fileSystem);
        var state = BackupState.CreateEmpty(RepositoryId, FullName)
            .WithArchive(BackupTarget.Wiki, CreateRecord(_otherFile), FullName);

        await store.ReplaceAsync(ArchiveFolder, state, TestContext.Current.CancellationToken);

        var loaded = await store.LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken);
        loaded.Repository.Archives.ShouldBeEmpty();
        loaded.Wiki.Archives.ShouldBe([CreateRecord(_otherFile)]);
        fileSystem.File.ReadAllText(BackupPath).ShouldBe(DocumentedSample);
        fileSystem.File.ReadAllText(StatePath).ShouldEndWith("}\r\n");
    }

    [Fact]
    public async Task ReplaceAsync_StateWithoutArchive_WritesExpectedJson()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample, SampleArchiveFile);
        var state = BackupState.CreateEmpty(RepositoryId, FullName);
        var expected = """
            {
              "schemaVersion": 1,
              "repositoryId": 123456789,
              "fullName": "askrinnik/my-repo",
              "repository": { "archives": [] },
              "wiki": { "archives": [] }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n";

        await CreateStore(fileSystem).ReplaceAsync(ArchiveFolder, state, TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllText(StatePath).ShouldBe(expected);
    }

    [Fact]
    public async Task ReplaceAsync_NoStateFile_ThrowsAndCreatesNothing()
    {
        var fileSystem = Populate(new MockFileSystem(), null);

        var exception = await Should.ThrowAsync<BackupStateException>(() => CreateStore(fileSystem).ReplaceAsync(
            ArchiveFolder, BackupState.CreateEmpty(RepositoryId, FullName), TestContext.Current.CancellationToken));

        exception.StatePath.ShouldBe(StatePath);
        FilesInFolder(fileSystem).ShouldBeEmpty();
    }

    [Fact]
    public async Task ReplaceAsync_OtherRepositoryId_ThrowsAndLeavesFileUnchanged()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample, SampleArchiveFile);

        var exception = await Should.ThrowAsync<BackupStateException>(() => CreateStore(fileSystem).ReplaceAsync(
            ArchiveFolder, BackupState.CreateEmpty(RepositoryId + 1, FullName), TestContext.Current.CancellationToken));

        exception.Errors.ShouldHaveSingleItem().ShouldContain("another repository");
        fileSystem.File.ReadAllText(StatePath).ShouldBe(DocumentedSample);
        fileSystem.File.Exists(BackupPath).ShouldBeFalse();
    }

    [Fact]
    public async Task ReplaceAsync_UnreadableExistingFile_ThrowsAndLeavesFileUnchanged()
    {
        var fileSystem = Populate(new MockFileSystem(), "not json");

        await Should.ThrowAsync<BackupStateException>(() => CreateStore(fileSystem).ReplaceAsync(
            ArchiveFolder, BackupState.CreateEmpty(RepositoryId, FullName), TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(StatePath).ShouldBe("not json");
        fileSystem.File.Exists(BackupPath).ShouldBeFalse();
    }

    [Fact]
    public async Task ReplaceAsync_ReplaceFails_KeepsOriginalFile()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailReplace = true }, DocumentedSample, SampleArchiveFile);

        await Should.ThrowAsync<IOException>(() => CreateStore(fileSystem).ReplaceAsync(
            ArchiveFolder, BackupState.CreateEmpty(RepositoryId, FullName), TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(StatePath).ShouldBe(DocumentedSample);
        FilesInFolder(fileSystem).ShouldBe([StatePath, Path.Combine(ArchiveFolder, SampleArchiveFile)]);
    }

    [Fact]
    public async Task ReplaceAsync_Cancelled_KeepsOriginalFile()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample, SampleArchiveFile);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => CreateStore(fileSystem).ReplaceAsync(
            ArchiveFolder, BackupState.CreateEmpty(RepositoryId, FullName), cancellation.Token));

        fileSystem.File.ReadAllText(StatePath).ShouldBe(DocumentedSample);
        FilesInFolder(fileSystem).ShouldBe([StatePath, Path.Combine(ArchiveFolder, SampleArchiveFile)]);
    }
}
