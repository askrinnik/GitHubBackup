using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Clones;
using GitHubBackup.Core.State;
using GitHubBackup.Infrastructure.Clones;
using GitHubBackup.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Infrastructure.Tests.Clones;

/// <summary>
/// Tests <see cref="JsonCloneLocationStore"/> with an in-memory file system.
/// </summary>
public sealed class JsonCloneLocationStoreTests
{
    private const string _sourcesRoot = @"C:\app\sources";
    private const string _path = @"C:\app\sources\.clone-locations.json";
    private const int _notReadEventId = 3;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<JsonCloneLocationStore> _logger = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadAsync_FileMissing_ReturnsEmptyWithoutWarning()
    {
        var locations = await CreateStore().LoadAsync(_sourcesRoot, CancellationToken);

        locations.Entries.ShouldBeEmpty();
        _logger.Collector.GetSnapshot().ShouldNotContain(record => record.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("""{ "schemaVersion": 2, "clones": {} }""")]
    public async Task LoadAsync_FileUnreadable_ReturnsEmptyAndWarns(string content)
    {
        _fileSystem.AddFile(_path, new MockFileData(content));

        var locations = await CreateStore().LoadAsync(_sourcesRoot, CancellationToken);

        locations.Entries.ShouldBeEmpty();
        var record = _logger.Collector.GetSnapshot().Where(entry => entry.Id.Id == _notReadEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_path);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_ReturnsSameLocations()
    {
        var locations = new CloneLocations();
        locations.Set(7, BackupTarget.Repository, @"D:\Work\App", "https://github.com/o/r.git");
        locations.Set(7, BackupTarget.Wiki, @"D:\Work\App.wiki", "https://github.com/o/r.git");
        locations.Set(3, BackupTarget.Repository, @"C:\app\sources\octo\lib", "https://github.com/o/r.git");

        await CreateStore().SaveAsync(_sourcesRoot, locations, CancellationToken);
        var loaded = await CreateStore().LoadAsync(_sourcesRoot, CancellationToken);

        loaded.Entries.ShouldBe(locations.Entries, ignoreOrder: true);
        loaded.IsChanged.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_Locations_WritesIndentedJsonOrderedById()
    {
        var locations = new CloneLocations();
        locations.Set(7, BackupTarget.Repository, @"D:\Work\App", "https://github.com/o/r.git");
        locations.Set(3, BackupTarget.Wiki, @"C:\app\sources\octo\lib.wiki", "https://github.com/o/r.git");
        var expected = """
            {
              "schemaVersion": 1,
              "clones": {
                "3": {
                  "repository": null,
                  "wiki": "C:\\app\\sources\\octo\\lib.wiki",
                  "repositoryUrl": null,
                  "wikiUrl": "https://github.com/o/r.git"
                },
                "7": {
                  "repository": "D:\\Work\\App",
                  "wiki": null,
                  "repositoryUrl": "https://github.com/o/r.git",
                  "wikiUrl": null
                }
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n";

        await CreateStore().SaveAsync(_sourcesRoot, locations, CancellationToken);

        _fileSystem.File.ReadAllText(_path).ShouldBe(expected);
    }

    [Fact]
    public async Task LoadAsync_EntryWithRelativeFolder_DropsItKeepsOthersAndWarns()
    {
        _fileSystem.AddFile(_path, new MockFileData("""
            {
              "schemaVersion": 1,
              "clones": {
                "1": { "repository": "Work\\App", "wiki": null },
                "2": { "repository": "D:\\Work\\Lib", "wiki": "D:\\Work\\Lib.wiki" }
              }
            }
            """));

        var locations = await CreateStore().LoadAsync(_sourcesRoot, CancellationToken);

        locations.Entries.Keys.ShouldBe([2L]);
        _logger.Collector.GetSnapshot().Where(entry => entry.Id.Id == _notReadEventId).ShouldHaveSingleItem()
            .Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task SaveAsync_FileExists_ReplacesItAndKeepsPreviousVersion()
    {
        _fileSystem.AddFile(_path, new MockFileData("""{ "schemaVersion": 1, "clones": {} }"""));
        var locations = new CloneLocations();
        locations.Set(1, BackupTarget.Repository, @"D:\Work\App", "https://github.com/o/r.git");

        await CreateStore().SaveAsync(_sourcesRoot, locations, CancellationToken);

        _fileSystem.File.ReadAllText(_path + AtomicFileWriter.BackupExtension).ShouldBe("""{ "schemaVersion": 1, "clones": {} }""");
        _fileSystem.File.ReadAllText(_path).ShouldContain(@"D:\\Work\\App");
        _fileSystem.AllFiles.ShouldNotContain(file => file.EndsWith(AtomicFileWriter.TemporaryExtension, StringComparison.Ordinal));
    }

    /// <summary>
    /// Creates the store over the in-memory file system.
    /// </summary>
    /// <returns>The store.</returns>
    private JsonCloneLocationStore CreateStore() => new(_fileSystem, new AtomicFileWriter(_fileSystem), _logger);
}
