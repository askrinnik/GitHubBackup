using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.History;
using GitHubBackup.Core.State;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.History;

/// <summary>
/// Tests <see cref="ArchiveHistoryProvider"/> over a substituted state store and a mock file system.
/// </summary>
public sealed class ArchiveHistoryProviderTests
{
    private const string _folder = @"C:\backup\octo\tool";

    private const long _repositoryId = 7;

    private const string _fullName = "octo/tool";

    private static readonly DateTimeOffset _firstAt = new(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(3));

    private static readonly DateTimeOffset _secondAt = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(3));

    private static readonly DateTimeOffset _wikiAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(3));

    private readonly IBackupStateStore _stateStore = Substitute.For<IBackupStateStore>();

    private readonly MockFileSystem _fileSystem = new();

    private readonly ArchiveHistoryProvider _provider;

    /// <summary>
    /// Creates the provider over an existing, empty archive folder and a state store that returns an empty state.
    /// </summary>
    public ArchiveHistoryProviderTests()
    {
        _fileSystem.AddDirectory(_folder);
        UseState(BackupState.CreateEmpty(_repositoryId, _fullName));
        _provider = new(_stateStore, _fileSystem);
    }

    [Fact]
    public async Task LoadAsync_NoArchives_ReturnsEmptyHistoryWithFolder()
    {
        var history = await LoadAsync();

        history.ArchiveFolder.ShouldBe(_folder);
        history.FolderExists.ShouldBeTrue();
        history.Archives.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoadAsync_RepositoryAndWikiArchives_ReturnsBothNewestFirst()
    {
        UseState(BackupState.CreateEmpty(_repositoryId, _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("first.zip", _firstAt, ("heads/main", "a1")), _fullName)
            .WithArchive(BackupTarget.Wiki, CreateRecord("wiki.zip", _wikiAt, ("heads/master", "w1")), _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("second.zip", _secondAt, ("heads/main", "a2")), _fullName));

        var history = await LoadAsync();

        history.Archives.Select(entry => entry.File).ShouldBe(["second.zip", "wiki.zip", "first.zip"]);
        history.Archives.Select(entry => entry.Target)
            .ShouldBe([BackupTarget.Repository, BackupTarget.Wiki, BackupTarget.Repository]);
    }

    [Fact]
    public async Task LoadAsync_Archive_CopiesRecordAndBuildsFullPath()
    {
        var record = new ArchiveRecord("tool.zip", _firstAt, "main", "0123456789abcdef", 2048, RefSnapshot.Empty);
        UseState(BackupState.CreateEmpty(_repositoryId, _fullName).WithArchive(BackupTarget.Repository, record, _fullName));

        var entry = (await LoadAsync()).Archives.ShouldHaveSingleItem();

        entry.ShouldBe(new ArchiveHistoryEntry(
            BackupTarget.Repository,
            "tool.zip",
            @"C:\backup\octo\tool\tool.zip",
            _firstAt,
            "main",
            "0123456789abcdef",
            2048,
            entry.ChangedRefs,
            false));
    }

    [Fact]
    public async Task LoadAsync_FirstArchive_EveryRefChanged()
    {
        UseState(BackupState.CreateEmpty(_repositoryId, _fullName).WithArchive(
            BackupTarget.Repository,
            CreateRecord("first.zip", _firstAt, ("heads/main", "a1"), ("tags/v1", "t1")),
            _fullName));

        var entry = (await LoadAsync()).Archives.ShouldHaveSingleItem();

        entry.ChangedRefs.ShouldBe(["heads/main", "tags/v1"]);
    }

    [Fact]
    public async Task LoadAsync_NextArchive_ChangedRefsAgainstPreviousArchive()
    {
        UseState(BackupState.CreateEmpty(_repositoryId, _fullName)
            .WithArchive(
                BackupTarget.Repository,
                CreateRecord("first.zip", _firstAt, ("heads/main", "a1"), ("heads/old", "o1"), ("tags/v1", "t1")),
                _fullName)
            .WithArchive(
                BackupTarget.Repository,
                CreateRecord("second.zip", _secondAt, ("heads/main", "a2"), ("heads/new", "n1"), ("tags/v1", "t1")),
                _fullName));

        var history = await LoadAsync();

        history.Archives[0].File.ShouldBe("second.zip");
        history.Archives[0].ChangedRefs.ShouldBe(["heads/main", "heads/new", "heads/old"]);
    }

    [Fact]
    public async Task LoadAsync_WikiArchive_ComparedOnlyWithWikiArchives()
    {
        UseState(BackupState.CreateEmpty(_repositoryId, _fullName)
            .WithArchive(BackupTarget.Wiki, CreateRecord("wiki-1.zip", _firstAt, ("heads/master", "w1")), _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("repo.zip", _wikiAt, ("heads/master", "r1")), _fullName)
            .WithArchive(BackupTarget.Wiki, CreateRecord("wiki-2.zip", _secondAt, ("heads/master", "w1")), _fullName));

        var history = await LoadAsync();

        var wiki = history.Archives.Single(entry => entry.File == "wiki-2.zip");
        wiki.ChangedRefs.ShouldBeEmpty();
        history.Archives.Single(entry => entry.File == "repo.zip").ChangedRefs.ShouldBe(["heads/master"]);
    }

    [Fact]
    public async Task LoadAsync_ArchiveFileExists_FileExistsTrue()
    {
        _fileSystem.AddFile(@"C:\backup\octo\tool\first.zip", new MockFileData([1, 2, 3]));
        UseState(BackupState.CreateEmpty(_repositoryId, _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("first.zip", _firstAt), _fullName));

        var entry = (await LoadAsync()).Archives.ShouldHaveSingleItem();

        entry.FileExists.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_ArchiveFileMissing_FileExistsFalse()
    {
        _fileSystem.AddFile(@"C:\backup\octo\tool\second.zip", new MockFileData([1]));
        UseState(BackupState.CreateEmpty(_repositoryId, _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("first.zip", _firstAt), _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("second.zip", _secondAt), _fullName));

        var history = await LoadAsync();

        history.Archives.Select(entry => (entry.File, entry.FileExists)).ShouldBe([("second.zip", true), ("first.zip", false)]);
    }

    [Fact]
    public async Task LoadAsync_FolderMissing_FolderExistsFalse()
    {
        _fileSystem.Directory.Delete(_folder);

        var history = await LoadAsync();

        history.FolderExists.ShouldBeFalse();
        history.Archives.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoadAsync_StateStoreThrows_PropagatesBackupStateException()
    {
        var error = new BackupStateException(@"C:\backup\octo\tool\.backup-state.json", ["The file is not valid JSON."]);
        _stateStore.LoadAsync(_folder, _repositoryId, _fullName, Arg.Any<CancellationToken>()).ThrowsAsync(error);

        var exception = await Should.ThrowAsync<BackupStateException>(LoadAsync);

        exception.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task LoadAsync_DifferentOffsets_OrdersByInstant()
    {
        // 09:00 at +00:00 is later than 10:00 at +03:00, although its local time reads earlier.
        var later = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var earlier = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(3));
        UseState(BackupState.CreateEmpty(_repositoryId, _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("earlier.zip", earlier), _fullName)
            .WithArchive(BackupTarget.Wiki, CreateRecord("later.zip", later), _fullName));

        var history = await LoadAsync();

        history.Archives.Select(entry => entry.File).ShouldBe(["later.zip", "earlier.zip"]);
    }

    /// <summary>
    /// Loads the history of the test repository.
    /// </summary>
    /// <returns>The history.</returns>
    private Task<ArchiveHistory> LoadAsync() =>
        _provider.LoadAsync(_folder, _repositoryId, _fullName, TestContext.Current.CancellationToken);

    /// <summary>
    /// Makes the state store return <paramref name="state"/> for the test repository.
    /// </summary>
    /// <param name="state">The state.</param>
    private void UseState(BackupState state) =>
        _stateStore.LoadAsync(_folder, _repositoryId, _fullName, Arg.Any<CancellationToken>()).Returns(state);

    /// <summary>
    /// Creates an archive record with the given refs.
    /// </summary>
    /// <param name="file">The file name of the archive.</param>
    /// <param name="createdAt">The time the archive was created.</param>
    /// <param name="refs">The refs as name and object id.</param>
    /// <returns>The record.</returns>
    private static ArchiveRecord CreateRecord(string file, DateTimeOffset createdAt, params (string Name, string ObjectId)[] refs) =>
        new(file, createdAt, "main", "a1b2c3", 10, new(refs.Select(pair => KeyValuePair.Create(pair.Name, pair.ObjectId))));
}
