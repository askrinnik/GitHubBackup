using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Ui;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="JsonUiSettingsStore"/> over <see cref="UserSettingsFile"/>.
/// </summary>
public sealed class JsonUiSettingsStoreTests : IDisposable
{
    private const string _contentRootPath = @"C:\app";
    private const string _filePath = @"C:\app\" + GitHubBackupHostBuilder.UserSettingsFileName;

    private readonly MockFileSystem _fileSystem = new();

    private readonly UserSettingsFile _file;

    private readonly JsonUiSettingsStore _store;

    /// <summary>
    /// Creates the content root, the user settings file and the subject.
    /// </summary>
    public JsonUiSettingsStoreTests()
    {
        _fileSystem.AddDirectory(_contentRootPath);
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRootPath);
        _file = new(_fileSystem, environment, new AtomicFileWriter(_fileSystem));
        _store = new(_file);
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsDefault() =>
        (await _store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBe(UiSettings.Default);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "Tools": { "GitPath": null } }""")]
    [InlineData("""{ "Ui": "Dark" }""")]
    [InlineData("""{ "Ui": null }""")]
    public async Task LoadAsync_FileWithoutUsableUiSection_ReturnsDefault(string json)
    {
        _fileSystem.AddFile(_filePath, new MockFileData(json));

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBe(UiSettings.Default);
    }

    [Fact]
    public async Task LoadAsync_CompleteSection_ReturnsValues()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "Ui": {
                "Theme": "Dark",
                "MainWindow": { "Left": -10.5, "Top": 20, "Width": 1200, "Height": 800, "IsMaximized": true,
                                "LogPanelHeight": 250, "SummaryPanelHeight": 300 }
              }
            }
            """));

        var settings = await _store.LoadAsync(TestContext.Current.CancellationToken);

        settings.ShouldBe(new UiSettings(UiTheme.Dark, new(-10.5, 20, 1200, 800, true), 250, 300));
    }

    [Theory]
    [InlineData("\"light\"", UiTheme.Light)]
    [InlineData("\"DARK\"", UiTheme.Dark)]
    [InlineData("\"System\"", UiTheme.System)]
    [InlineData("\"Blue\"", UiTheme.System)]
    [InlineData("\"1\"", UiTheme.System)]
    [InlineData("2", UiTheme.System)]
    [InlineData("null", UiTheme.System)]
    public async Task LoadAsync_Theme_ReadsNameWithoutCaseOrReturnsSystem(string value, UiTheme expected)
    {
        _fileSystem.AddFile(_filePath, new MockFileData($$"""{ "ui": { "theme": {{value}} } }"""));

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).Theme.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{ "Top": 20, "Width": 1200, "Height": 800 }""")]
    [InlineData("""{ "Left": "10", "Top": 20, "Width": 1200, "Height": 800 }""")]
    [InlineData("""{ "Left": 10, "Top": 20, "Width": 100, "Height": 800 }""")]
    [InlineData("""{ "Left": 10, "Top": 20, "Width": 1200, "Height": 20000 }""")]
    public async Task LoadAsync_InvalidPlacement_ReturnsDefaultPlacementAndKeepsTheme(string window)
    {
        _fileSystem.AddFile(_filePath, new MockFileData($$"""{ "Ui": { "Theme": "Light", "MainWindow": {{window}} } }"""));

        var settings = await _store.LoadAsync(TestContext.Current.CancellationToken);

        settings.ShouldBe(UiSettings.Default with { Theme = UiTheme.Light });
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("null")]
    public async Task LoadAsync_IsMaximizedNotBoolean_ReturnsFalse(string value)
    {
        _fileSystem.AddFile(_filePath, new MockFileData(
            $$"""{ "Ui": { "MainWindow": { "Left": 1, "Top": 2, "Width": 800, "Height": 600, "IsMaximized": {{value}} } } }"""));

        var settings = await _store.LoadAsync(TestContext.Current.CancellationToken);

        settings.MainWindow.ShouldBe(new WindowPlacement(1, 2, 800, 600, false));
    }

    [Fact]
    public async Task LoadAsync_InvalidPanelHeights_ReturnsDefaultForEachInvalidHeight()
    {
        _fileSystem.AddFile(_filePath, new MockFileData(
            """{ "Ui": { "MainWindow": { "LogPanelHeight": "high", "SummaryPanelHeight": 5000 } } }"""));

        var settings = await _store.LoadAsync(TestContext.Current.CancellationToken);

        settings.LogPanelHeight.ShouldBe(UiSettings.DefaultLogPanelHeight);
        settings.SummaryPanelHeight.ShouldBe(UiSettings.DefaultSummaryPanelHeight);
    }

    [Fact]
    public async Task LoadAsync_ByteOrderMarkAndComments_ReadsSection()
    {
        _fileSystem.AddFile(_filePath, new MockFileData(
            [0xEF, 0xBB, 0xBF, .. """{ /* edited */ "Ui": { "Theme": "Dark", }, }"""u8.ToArray()]));

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).Theme.ShouldBe(UiTheme.Dark);
    }

    [Fact]
    public async Task SaveAsync_NoFile_WritesUiSectionOnly()
    {
        await _store.SaveAsync(
            new(UiTheme.Dark, new(100, 50.5, 1000, 700, true), 160, 220), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Ui": {
                "Theme": "Dark",
                "MainWindow": {
                  "Left": 100,
                  "Top": 50.5,
                  "Width": 1000,
                  "Height": 700,
                  "IsMaximized": true,
                  "LogPanelHeight": 160,
                  "SummaryPanelHeight": 220
                }
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Fact]
    public async Task SaveAsync_DefaultPlacementAndInvalidHeight_WritesHeightsOnlyWithDefault()
    {
        await _store.SaveAsync(UiSettings.Default with { SummaryPanelHeight = 1 }, TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Ui": {
                "Theme": "System",
                "MainWindow": {
                  "LogPanelHeight": 160,
                  "SummaryPanelHeight": 220
                }
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Fact]
    public async Task SaveAsync_ToolsSectionInFile_KeepsToolsAsWritten()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "Tools": { "SevenZipPath": "D:\\7z\\7z.exe" },
              "Ui": { "Theme": "Dark" }
            }
            """));

        await _store.SaveAsync(UiSettings.Default with { Theme = UiTheme.Light }, TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Tools": {
                "SevenZipPath": "D:\\7z\\7z.exe"
              },
              "Ui": {
                "Theme": "Light",
                "MainWindow": {
                  "LogPanelHeight": 160,
                  "SummaryPanelHeight": 220
                }
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Fact]
    public async Task SaveAsync_ToolsSectionWithUnknownKey_KeepsOnlyKnownToolKeys()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            { "tools": { "GitPath": null, "ApiBaseUrl": "https://example.com/api", "SevenZipPath": { "nested": 1 } } }
            """));

        await _store.SaveAsync(UiSettings.Default, TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Tools": {
                "GitPath": null
              },
              "Ui": {
                "Theme": "System",
                "MainWindow": {
                  "LogPanelHeight": 160,
                  "SummaryPanelHeight": 220
                }
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Fact]
    public async Task SaveAsync_FileExists_KeepsPreviousAsBackup()
    {
        const string previous = """{ "Ui": { "Theme": "Dark" } }""";
        _fileSystem.AddFile(_filePath, new MockFileData(previous));

        await _store.SaveAsync(UiSettings.Default, TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath + AtomicFileWriter.BackupExtension).ShouldBe(previous);
        (await _store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBe(UiSettings.Default);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public async Task SaveAsync_EmptyFile_WritesUiSection(string content)
    {
        _fileSystem.AddFile(_filePath, new MockFileData(content));

        await _store.SaveAsync(UiSettings.Default with { Theme = UiTheme.Dark }, TestContext.Current.CancellationToken);

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).Theme.ShouldBe(UiTheme.Dark);
    }

    [Theory]
    [InlineData("{ \"Tools\": { \"GitPath\": \"C:\\\\Git\\\\git.exe\" }")]
    [InlineData("[ 1, 2 ]")]
    [InlineData("\"text\"")]
    public async Task SaveAsync_FileNotJsonObject_ThrowsAndLeavesFileIntact(string content)
    {
        _fileSystem.AddFile(_filePath, new MockFileData(content));

        var exception = await Should.ThrowAsync<IOException>(
            () => _store.SaveAsync(UiSettings.Default, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(_filePath);
        _fileSystem.File.ReadAllText(_filePath).ShouldBe(content);
        _fileSystem.File.Exists(_filePath + AtomicFileWriter.BackupExtension).ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_FileLocked_ThrowsAndLeavesFileIntact()
    {
        const string content = """{ "Tools": { "GitPath": "C:\\Git\\git.exe" } }""";
        var locked = new MockFileData(content) { AllowedFileShare = FileShare.None };
        _fileSystem.AddFile(_filePath, locked);

        await Should.ThrowAsync<IOException>(() => _store.SaveAsync(UiSettings.Default, TestContext.Current.CancellationToken));

        locked.TextContents.ShouldBe(content);
        _fileSystem.File.Exists(_filePath + AtomicFileWriter.BackupExtension).ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_FileLocked_ReturnsDefault()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""{ "Ui": { "Theme": "Dark" } }""") { AllowedFileShare = FileShare.None });

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBe(UiSettings.Default);
    }

    [Fact]
    public async Task SaveAsync_Cancelled_ThrowsAndWritesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => _store.SaveAsync(UiSettings.Default, cancellation.Token));

        _fileSystem.File.Exists(_filePath).ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_ReturnsSavedSettings()
    {
        var saved = new UiSettings(UiTheme.Light, new(-1920, 0, 640.5, 480, false), 61, 1999);

        await _store.SaveAsync(saved, TestContext.Current.CancellationToken);

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBe(saved);
    }
}
