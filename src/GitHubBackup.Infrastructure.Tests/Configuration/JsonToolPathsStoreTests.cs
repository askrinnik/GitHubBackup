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
public sealed class JsonToolPathsStoreTests
{
    private const string _contentRootPath = @"C:\app";
    private const string _filePath = @"C:\app\" + GitHubBackupHostBuilder.UserSettingsFileName;

    private readonly MockFileSystem _fileSystem = new();

    /// <summary>
    /// Creates the content root of the store.
    /// </summary>
    public JsonToolPathsStoreTests() => _fileSystem.AddDirectory(_contentRootPath);

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
        _fileSystem.AddFile(_filePath, new MockFileData("previous"));

        await CreateStore().SaveAsync(new(@"D:\Git\git.exe", null), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath + AtomicFileWriter.BackupExtension).ShouldBe("previous");
        _fileSystem.File.ReadAllText(_filePath).ShouldContain(@"D:\\Git\\git.exe");
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
    private JsonToolPathsStore CreateStore(ToolsOptions? options = null)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRootPath);
        return new(Options.Create(options ?? new()), environment, new AtomicFileWriter(_fileSystem));
    }
}
