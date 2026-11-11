using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.State;
using GitHubBackup.Infrastructure.State;
using static GitHubBackup.Infrastructure.Tests.State.BackupStateSamples;

namespace GitHubBackup.Infrastructure.Tests.State;

/// <summary>
/// Tests <see cref="JsonBackupStateStore.LoadAsync"/>.
/// </summary>
public sealed class JsonBackupStateStoreTests
{
    /// <summary>A text that must never appear in an error message, because it comes from the file.</summary>
    private const string _secretLikeValue = "ghp_notARealTokenButQuoted";

    /// <summary>Gets documents that are not a valid state file, each with a short description.</summary>
    public static TheoryData<string, string> InvalidDocuments => new()
    {
        { "broken JSON", """{ "schemaVersion": 1, """ },
        { "schema version 2", DocumentedSample.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal) },
        { "no wiki", DocumentedSample.Replace("  },", "  }", StringComparison.Ordinal).Replace("\"wiki\": { \"archives\": [] }", "", StringComparison.Ordinal) },
        { "unknown property", DocumentedSample.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"extra\": true,", StringComparison.Ordinal) },
        { "ref without prefix", DocumentedSample.Replace("\"heads/dev\"", "\"dev\"", StringComparison.Ordinal) },
        { "repeated ref", DocumentedSample.Replace("\"heads/dev\"", "\"heads/main\"", StringComparison.Ordinal) },
        { "null file", DocumentedSample.Replace("\"file\": \"askrinnik_my-repo_20261002-143015_a1b2c3d4e5.zip\"", "\"file\": null", StringComparison.Ordinal) },
        { "file with a folder", DocumentedSample.Replace("\"file\": \"askrinnik", "\"file\": \"..\\\\askrinnik", StringComparison.Ordinal) },
        { "time without offset", DocumentedSample.Replace("+03:00", "", StringComparison.Ordinal) },
        { "unknown section property", DocumentedSample.Replace("{ \"archives\": [] }", "{ \"archives\": [], \"extra\": 1 }", StringComparison.Ordinal) },
        { "null document", "null" },
    };

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyStateWithGivenIdentity()
    {
        var fileSystem = Populate(new MockFileSystem(), null);

        var state = await CreateStore(fileSystem).LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken);

        state.ShouldBeEquivalentTo(BackupState.CreateEmpty(RepositoryId, FullName));
        state.Repository.Archives.ShouldBeEmpty();
        state.Wiki.Archives.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoadAsync_MissingFolder_ReturnsEmptyState()
    {
        var fileSystem = new MockFileSystem();

        var state = await CreateStore(fileSystem).LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken);

        state.SchemaVersion.ShouldBe(BackupState.CurrentSchemaVersion);
        state.RepositoryId.ShouldBe(RepositoryId);
        state.FullName.ShouldBe(FullName);
        state.Repository.Archives.ShouldBeEmpty();
        state.Wiki.Archives.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoadAsync_DocumentedSample_ReadsAllFields()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample);

        var state = await CreateStore(fileSystem).LoadAsync(ArchiveFolder, RepositoryId, "other/name", TestContext.Current.CancellationToken);

        state.SchemaVersion.ShouldBe(1);
        state.RepositoryId.ShouldBe(RepositoryId);
        state.FullName.ShouldBe(FullName);
        state.Wiki.Archives.ShouldBeEmpty();
        var record = state.Repository.Archives.ShouldHaveSingleItem();
        record.File.ShouldBe(SampleArchiveFile);
        record.CreatedAt.ShouldBe(new(2026, 10, 2, 14, 30, 15, TimeSpan.FromHours(3)));
        record.CreatedAt.Offset.ShouldBe(TimeSpan.FromHours(3));
        record.Branch.ShouldBe("main");
        record.HeadCommit.ShouldBe("a1b2c3d4e5f60718293a4b5c6d7e8f9012345678");
        record.SizeBytes.ShouldBe(10485760);
        record.Refs.ShouldBe(new RefSnapshot(
        [
            new("heads/main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678"),
            new("heads/dev", "77f0e1d2c3b4a5968778695a4b3c2d1e0f9a8b7c"),
            new("tags/v1.0", "c3d4e5f60718293a4b5c6d7e8f9012345678a1b2"),
        ]));
    }

    [Fact]
    public async Task LoadAsync_UtcTime_ReadsZeroOffset()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample.Replace("14:30:15+03:00", "11:30:15Z", StringComparison.Ordinal));

        var state = await CreateStore(fileSystem).LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken);

        state.Repository.Archives[0].CreatedAt.ShouldBe(new(2026, 10, 2, 11, 30, 15, TimeSpan.Zero));
        state.Repository.Archives[0].CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task LoadAsync_SavedAndLoaded_RoundTrips()
    {
        var fileSystem = Populate(new MockFileSystem(), null, SampleArchiveFile, "wiki.zip");
        var store = CreateStore(fileSystem);
        await store.AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Repository, CreateSampleRecord(), TestContext.Current.CancellationToken);
        var written = await store.AppendAsync(
            ArchiveFolder, RepositoryId, FullName, BackupTarget.Wiki, CreateRecord("wiki.zip"), TestContext.Current.CancellationToken);

        var loaded = await store.LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken);

        loaded.SchemaVersion.ShouldBe(written.SchemaVersion);
        loaded.RepositoryId.ShouldBe(written.RepositoryId);
        loaded.FullName.ShouldBe(written.FullName);
        loaded.Repository.Archives.ShouldBe(written.Repository.Archives);
        loaded.Wiki.Archives.ShouldBe(written.Wiki.Archives);
    }

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public async Task LoadAsync_InvalidDocument_ThrowsBackupStateException(string description, string json)
    {
        var fileSystem = Populate(new MockFileSystem(), json);

        var exception = await Should.ThrowAsync<BackupStateException>(
            () => CreateStore(fileSystem).LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken),
            description);

        exception.StatePath.ShouldBe(StatePath);
        exception.Errors.ShouldHaveSingleItem().ShouldContain(StatePath);
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_MessageDoesNotQuoteContent()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample.Replace("\"branch\": \"main\"", $"\"branch\": {_secretLikeValue}", StringComparison.Ordinal));

        var exception = await Should.ThrowAsync<BackupStateException>(
            () => CreateStore(fileSystem).LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken));

        exception.Message.ShouldNotContain(_secretLikeValue);
        exception.Errors.ShouldHaveSingleItem().ShouldContain("line 10,");
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_InvalidRefName_MessageDoesNotQuoteContent()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample.Replace("\"heads/dev\"", $"\"{_secretLikeValue}\"", StringComparison.Ordinal));

        var exception = await Should.ThrowAsync<BackupStateException>(
            () => CreateStore(fileSystem).LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken));

        exception.Message.ShouldNotContain(_secretLikeValue);
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_OtherRepositoryId_ThrowsBackupStateException()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample);

        var exception = await Should.ThrowAsync<BackupStateException>(
            () => CreateStore(fileSystem).LoadAsync(ArchiveFolder, RepositoryId + 1, FullName, TestContext.Current.CancellationToken));

        exception.StatePath.ShouldBe(StatePath);
        exception.Message.ShouldContain("another repository");
    }

    [Fact]
    public async Task LoadAsync_UnreadableFile_ThrowsBackupStateException()
    {
        var fileSystem = Populate(new MockFileSystem(), DocumentedSample);
        fileSystem.GetFile(StatePath).AllowedFileShare = FileShare.None;

        var exception = await Should.ThrowAsync<BackupStateException>(
            () => CreateStore(fileSystem).LoadAsync(ArchiveFolder, RepositoryId, FullName, TestContext.Current.CancellationToken));

        exception.StatePath.ShouldBe(StatePath);
        exception.InnerException.ShouldBeAssignableTo<IOException>();
    }
}
