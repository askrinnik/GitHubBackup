using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="JsonToolPathsStore"/>.
/// </summary>
public sealed class JsonToolPathsStoreTests : IDisposable
{
    private const string _contentRootPath = @"C:\app";
    private const string _filePath = @"C:\app\" + GitHubBackupHostBuilder.UserSettingsFileName;

    private readonly MockFileSystem _fileSystem = new();

    private readonly UserSettingsFile _file;

    /// <summary>
    /// Creates the content root and the user settings file of the store.
    /// </summary>
    public JsonToolPathsStoreTests()
    {
        _fileSystem.AddDirectory(_contentRootPath);
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRootPath);
        _file = new(_fileSystem, environment, new AtomicFileWriter(_fileSystem));
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    [Fact]
    public void Current_Options_ReturnsConfiguredPaths()
    {
        var store = CreateStore(new() { GitPath = @"C:\Git\git.exe", SevenZipPath = @"C:\7z\7z.exe" });

        store.Current.ShouldBe(new ToolPaths(@"C:\Git\git.exe", @"C:\7z\7z.exe"));
    }

    [Fact]
    public async Task SaveAsync_BothPaths_WritesToolsSection()
    {
        await CreateStore().SaveAsync(new(@"D:\Git\git.exe", @"D:\Архив\7z.exe"), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Tools": {
                "GitPath": "D:\\Git\\git.exe",
                "SevenZipPath": "D:\\Архив\\7z.exe"
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SaveAsync_PathNotSet_WritesNull(string? gitPath)
    {
        await CreateStore().SaveAsync(new(gitPath, null), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldContain("\"GitPath\": null");
        _fileSystem.File.ReadAllText(_filePath).ShouldContain("\"SevenZipPath\": null");
    }

    [Fact]
    public async Task SaveAsync_FileExists_KeepsPreviousAsBackup()
    {
        const string previous = """{ "Tools": { "GitPath": null } }""";
        _fileSystem.AddFile(_filePath, new MockFileData(previous));

        await CreateStore().SaveAsync(new(@"D:\Git\git.exe", null), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath + AtomicFileWriter.BackupExtension).ShouldBe(previous);
        _fileSystem.File.ReadAllText(_filePath).ShouldContain(@"D:\\Git\\git.exe");
    }

    [Theory]
    [InlineData("previous")]
    [InlineData("{ \"Ui\": { \"Theme\": \"Dark\" }")]
    [InlineData("[]")]
    public async Task SaveAsync_FileNotJsonObject_ThrowsAndLeavesFileIntact(string content)
    {
        _fileSystem.AddFile(_filePath, new MockFileData(content));

        var exception = await Should.ThrowAsync<IOException>(
            () => CreateStore().SaveAsync(new(@"D:\Git\git.exe", null), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(_filePath);
        _fileSystem.File.ReadAllText(_filePath).ShouldBe(content);
        _fileSystem.File.Exists(_filePath + AtomicFileWriter.BackupExtension).ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_FileLocked_ThrowsAndLeavesFileIntact()
    {
        const string content = """{ "Ui": { "Theme": "Dark" } }""";
        var locked = new MockFileData(content) { AllowedFileShare = FileShare.None };
        _fileSystem.AddFile(_filePath, locked);

        await Should.ThrowAsync<IOException>(
            () => CreateStore().SaveAsync(new(@"D:\Git\git.exe", null), TestContext.Current.CancellationToken));

        locked.TextContents.ShouldBe(content);
        _fileSystem.File.Exists(_filePath + AtomicFileWriter.BackupExtension).ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_UiSectionInFile_KeepsKnownUiKeysOnlyAndDropsOtherSections()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "GitHub": { "ApiBaseUrl": "https://example.com/api" },
              "ui": { "Theme": "Dark", "Extra": 1, "MainWindow": { "LogPanelHeight": 300, "Opacity": 0.5 } },
              "Tools": { "GitPath": "C:\\Old\\git.exe" }
            }
            """));

        await CreateStore().SaveAsync(new(@"D:\Git\git.exe", null), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Tools": {
                "GitPath": "D:\\Git\\git.exe",
                "SevenZipPath": null
              },
              "Ui": {
                "Theme": "Dark",
                "MainWindow": {
                  "LogPanelHeight": 300
                }
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Fact]
    public async Task SaveAsync_RelativePath_ThrowsAndWritesNothing()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => CreateStore().SaveAsync(new(@"git\git.exe", null), TestContext.Current.CancellationToken));

        _fileSystem.File.Exists(_filePath).ShouldBeFalse();
    }

    [Fact]
    public void Validate_RelativePaths_ReturnsErrorPerKeyWithoutValue()
    {
        var errors = CreateStore().Validate(new("git.exe", @"7z\7z.exe"));

        errors.ShouldBe(
        [
            "Tools:GitPath must be an absolute path when set.",
            "Tools:SevenZipPath must be an absolute path when set.",
        ]);
        errors[0].ShouldStartWith(ToolPaths.GitPathKey);
        errors[1].ShouldStartWith(ToolPaths.SevenZipPathKey);
    }

    [Fact]
    public void Validate_AbsoluteOrUnsetPaths_ReturnsNoError() =>
        CreateStore().Validate(new(null, @"C:\7z\7z.exe")).ShouldBeEmpty();

    /// <summary>
    /// Creates the subject over the mock file system and <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The tool options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The subject.</returns>
    private JsonToolPathsStore CreateStore(ToolsOptions? options = null) => new(Options.Create(options ?? new()), _file);
}
