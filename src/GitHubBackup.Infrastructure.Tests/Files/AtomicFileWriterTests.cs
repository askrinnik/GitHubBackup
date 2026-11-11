using System.IO.Abstractions.TestingHelpers;
using System.Text;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Tests.BackupConfiguration;

namespace GitHubBackup.Infrastructure.Tests.Files;

/// <summary>
/// Tests <see cref="AtomicFileWriter"/>.
/// </summary>
public sealed class AtomicFileWriterTests
{
    /// <summary>The folder of the file in every test.</summary>
    private const string _folderPath = @"C:\data";

    /// <summary>The path of the file in every test.</summary>
    private const string _filePath = @"C:\data\file.json";

    /// <summary>The path of the copy of the previous file.</summary>
    private const string _backupPath = _filePath + AtomicFileWriter.BackupExtension;

    /// <summary>The content of the file before a write.</summary>
    private const string _originalContent = "original content";

    /// <summary>The content written by every test.</summary>
    private static readonly byte[] _newContent = Encoding.UTF8.GetBytes("new content");

    [Fact]
    public async Task WriteAsync_NoFile_CreatesFileWithoutBackup()
    {
        var fileSystem = Populate(new MockFileSystem(), original: null);

        await new AtomicFileWriter(fileSystem).WriteAsync(_filePath, _newContent, TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllBytes(_filePath).ShouldBe(_newContent);
        FilesInFolder(fileSystem).ShouldBe([_filePath]);
    }

    [Fact]
    public async Task WriteAsync_FileExists_ReplacesFileAndKeepsPreviousAsBackup()
    {
        var fileSystem = Populate(new MockFileSystem(), _originalContent);

        await new AtomicFileWriter(fileSystem).WriteAsync(_filePath, _newContent, TestContext.Current.CancellationToken);

        fileSystem.File.ReadAllBytes(_filePath).ShouldBe(_newContent);
        fileSystem.File.ReadAllText(_backupPath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([_filePath, _backupPath]);
    }

    [Fact]
    public async Task WriteAsync_WriteOfTemporaryFileFails_KeepsOriginalFileAndRemovesTemporaryFile()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailStreamWrite = true }, _originalContent);

        await Should.ThrowAsync<IOException>(
            () => new AtomicFileWriter(fileSystem).WriteAsync(_filePath, _newContent, TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(_filePath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([_filePath]);
    }

    [Fact]
    public async Task WriteAsync_ReplaceFails_KeepsOriginalFileAndRemovesTemporaryFile()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailReplace = true }, _originalContent);

        await Should.ThrowAsync<IOException>(
            () => new AtomicFileWriter(fileSystem).WriteAsync(_filePath, _newContent, TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(_filePath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([_filePath]);
    }

    [Fact]
    public async Task WriteAsync_ReplaceFailsAfterBackup_MovesNewContentToFinalName()
    {
        var fileSystem = Populate(new FaultInjectingFileSystem { FailReplaceAfterBackup = true }, _originalContent);

        await Should.ThrowAsync<IOException>(
            () => new AtomicFileWriter(fileSystem).WriteAsync(_filePath, _newContent, TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllBytes(_filePath).ShouldBe(_newContent);
        fileSystem.File.ReadAllText(_backupPath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([_filePath, _backupPath]);
    }

    [Fact]
    public async Task WriteAsync_ReplaceFailsAfterBackupAndMoveFails_RestoresOriginalFile()
    {
        var fileSystem = Populate(
            new FaultInjectingFileSystem { FailReplaceAfterBackup = true, FailMove = true }, _originalContent);

        await Should.ThrowAsync<IOException>(
            () => new AtomicFileWriter(fileSystem).WriteAsync(_filePath, _newContent, TestContext.Current.CancellationToken));

        fileSystem.File.ReadAllText(_filePath).ShouldBe(_originalContent);
        fileSystem.File.ReadAllText(_backupPath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([_filePath, _backupPath]);
    }

    [Fact]
    public async Task WriteAsync_Cancelled_KeepsOriginalFileAndRemovesTemporaryFile()
    {
        var fileSystem = Populate(new MockFileSystem(), _originalContent);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => new AtomicFileWriter(fileSystem).WriteAsync(_filePath, _newContent, cancellation.Token));

        fileSystem.File.ReadAllText(_filePath).ShouldBe(_originalContent);
        FilesInFolder(fileSystem).ShouldBe([_filePath]);
    }

    /// <summary>
    /// Adds the folder and, when given, the file with <paramref name="original"/> to <paramref name="fileSystem"/>.
    /// </summary>
    /// <typeparam name="TFileSystem">The type of the file system.</typeparam>
    /// <param name="fileSystem">The file system to populate.</param>
    /// <param name="original">The content of the file, or <see langword="null"/> for no file.</param>
    /// <returns>The same file system.</returns>
    private static TFileSystem Populate<TFileSystem>(TFileSystem fileSystem, string? original)
        where TFileSystem : MockFileSystem
    {
        fileSystem.AddDirectory(_folderPath);
        if (original is not null)
        {
            fileSystem.AddFile(_filePath, new MockFileData(original));
        }

        return fileSystem;
    }

    /// <summary>
    /// Returns the files in the folder of the file, in ordinal order.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <returns>The full paths of the files.</returns>
    private static string[] FilesInFolder(MockFileSystem fileSystem) =>
        [.. fileSystem.Directory.GetFiles(_folderPath).Order(StringComparer.Ordinal)];
}
