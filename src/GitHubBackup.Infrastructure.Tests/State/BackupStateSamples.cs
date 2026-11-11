using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.State;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.State;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Infrastructure.Tests.State;

/// <summary>
/// Supplies the documented <c>.backup-state.json</c> sample and builds stores over a mock file system.
/// </summary>
internal static class BackupStateSamples
{
    /// <summary>The archive folder of the repository in every test.</summary>
    public const string ArchiveFolder = @"C:\backup\askrinnik\my-repo";

    /// <summary>The path of the state file in every test.</summary>
    public const string StatePath = ArchiveFolder + @"\" + BackupState.FileName;

    /// <summary>The path of the copy of the previous state file.</summary>
    public const string BackupPath = StatePath + AtomicFileWriter.BackupExtension;

    /// <summary>The GitHub id of the repository in every test.</summary>
    public const long RepositoryId = 123456789;

    /// <summary>The full name of the repository in every test.</summary>
    public const string FullName = "askrinnik/my-repo";

    /// <summary>The file name of the archive of the documented sample.</summary>
    public const string SampleArchiveFile = "askrinnik_my-repo_20261002-143015_a1b2c3d4e5.zip";

    /// <summary>The sample of the documentation, verbatim.</summary>
    public const string DocumentedSample =
        """
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
                  "heads/main": "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
                  "heads/dev": "77f0e1d2c3b4a5968778695a4b3c2d1e0f9a8b7c",
                  "tags/v1.0": "c3d4e5f60718293a4b5c6d7e8f9012345678a1b2"
                }
              }
            ]
          },
          "wiki": { "archives": [] }
        }
        """;

    /// <summary>
    /// Creates the record that the documented sample describes.
    /// </summary>
    /// <returns>The record.</returns>
    public static ArchiveRecord CreateSampleRecord() => new(
        SampleArchiveFile,
        new(2026, 10, 2, 14, 30, 15, TimeSpan.FromHours(3)),
        "main",
        "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
        10485760,
        new RefSnapshot(
        [
            new("heads/main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678"),
            new("heads/dev", "77f0e1d2c3b4a5968778695a4b3c2d1e0f9a8b7c"),
            new("tags/v1.0", "c3d4e5f60718293a4b5c6d7e8f9012345678a1b2"),
        ]));

    /// <summary>
    /// Creates a valid record of an archive named <paramref name="file"/>.
    /// </summary>
    /// <param name="file">The file name of the archive.</param>
    /// <returns>The record.</returns>
    public static ArchiveRecord CreateRecord(string file) => new(
        file,
        new(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(2)),
        "main",
        "0123456789abcdef0123456789abcdef01234567",
        2048,
        new RefSnapshot([new("heads/main", "0123456789abcdef0123456789abcdef01234567")]));

    /// <summary>
    /// Creates a store over <paramref name="fileSystem"/>.
    /// </summary>
    /// <param name="fileSystem">The mock file system.</param>
    /// <returns>The store.</returns>
    public static JsonBackupStateStore CreateStore(MockFileSystem fileSystem) =>
        new(fileSystem, new(fileSystem), new FakeLogger<JsonBackupStateStore>());

    /// <summary>
    /// Adds the archive folder, the given archive files and, when given, the state file to
    /// <paramref name="fileSystem"/>.
    /// </summary>
    /// <typeparam name="TFileSystem">The type of the file system.</typeparam>
    /// <param name="fileSystem">The file system to populate.</param>
    /// <param name="stateJson">The text of the state file, or <see langword="null"/> for none.</param>
    /// <param name="archiveFiles">The file names of the archives in the archive folder.</param>
    /// <returns>The same file system.</returns>
    public static TFileSystem Populate<TFileSystem>(TFileSystem fileSystem, string? stateJson, params string[] archiveFiles)
        where TFileSystem : MockFileSystem
    {
        fileSystem.AddDirectory(ArchiveFolder);
        foreach (var archiveFile in archiveFiles)
        {
            fileSystem.AddFile(Path.Combine(ArchiveFolder, archiveFile), new MockFileData([1, 2, 3]));
        }

        if (stateJson is not null)
        {
            fileSystem.AddFile(StatePath, new MockFileData(stateJson));
        }

        return fileSystem;
    }

    /// <summary>
    /// Returns the files in the archive folder, in ordinal order.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <returns>The full paths of the files.</returns>
    public static string[] FilesInFolder(MockFileSystem fileSystem) =>
        [.. fileSystem.Directory.GetFiles(ArchiveFolder).Order(StringComparer.Ordinal)];
}
