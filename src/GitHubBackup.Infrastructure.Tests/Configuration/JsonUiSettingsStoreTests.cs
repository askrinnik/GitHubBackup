using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Ui;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="JsonUiSettingsStore"/> over <see cref="UiSettingsFile"/>.
/// </summary>
public sealed class JsonUiSettingsStoreTests : IDisposable
{
    private const string _contentRootPath = @"C:\app";
    private const string _filePath = @"C:\app\" + GitHubBackupHostBuilder.UiSettingsFileName;
    private const string _appSettingsPath = @"C:\app\" + GitHubBackupHostBuilder.AppSettingsFileName;
    private const string _legacyUserSettingsPath = @"C:\app\user-settings.json";

    private readonly MockFileSystem _fileSystem = new();

    private readonly UiSettingsFile _file;

    private readonly JsonUiSettingsStore _store;

    /// <summary>
    /// Creates the content root, the user interface settings file and the subject.
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
    public void FilePath_ContentRoot_IsUiSettingsFileInContentRoot() => _file.FilePath.ShouldBe(_filePath);

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsDefault() =>
        (await _store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBe(UiSettings.Default);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("\"Dark\"")]
    [InlineData("""{ "Tools": { "GitPath": null } }""")]
    [InlineData("""{ "MainWindow": null }""")]
    public async Task LoadAsync_FileWithoutUsableValues_ReturnsDefault(string json)
    {
        _fileSystem.AddFile(_filePath, new MockFileData(json));

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBe(UiSettings.Default);
    }

    [Fact]
    public async Task LoadAsync_CompleteFile_ReturnsValues()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "Theme": "Dark",
              "MainWindow": { "Left": -10.5, "Top": 20, "Width": 1200, "Height": 800, "IsMaximized": true,
                              "LogPanelHeight": 250, "SummaryPanelHeight": 300 }
            }
            """));

        var settings = await _store.LoadAsync(TestContext.Current.CancellationToken);

        settings.ShouldBe(new UiSettings(UiTheme.Dark, new(-10.5, 20, 1200, 800, true), 250, 300));
    }

    [Fact]
    public async Task LoadAsync_UiValuesInOtherFiles_ReturnsDefault()
    {
        const string ui = """{ "Ui": { "Theme": "Dark" }, "Theme": "Dark" }""";
        _fileSystem.AddFile(_appSettingsPath, new MockFileData(ui));
        _fileSystem.AddFile(_legacyUserSettingsPath, new MockFileData(ui));

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBe(UiSettings.Default);
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
        _fileSystem.AddFile(_filePath, new MockFileData($$"""{ "theme": {{value}} }"""));

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).Theme.ShouldBe(expected);
    }

    [Fact]
    public async Task LoadAsync_KeysInOtherCase_ReadsThem()
    {
        _fileSystem.AddFile(_filePath, new MockFileData(
            """{ "THEME": "Light", "mainwindow": { "left": 1, "TOP": 2, "width": 800, "height": 600, "logpanelheight": 100 } }"""));

        var settings = await _store.LoadAsync(TestContext.Current.CancellationToken);

        settings.ShouldBe(new UiSettings(UiTheme.Light, new(1, 2, 800, 600, false), 100, UiSettings.DefaultSummaryPanelHeight));
    }

    [Theory]
    [InlineData("""{ "Top": 20, "Width": 1200, "Height": 800 }""")]
    [InlineData("""{ "Left": "10", "Top": 20, "Width": 1200, "Height": 800 }""")]
    [InlineData("""{ "Left": 10, "Top": 20, "Width": 100, "Height": 800 }""")]
    [InlineData("""{ "Left": 10, "Top": 20, "Width": 1200, "Height": 20000 }""")]
    public async Task LoadAsync_InvalidPlacement_ReturnsDefaultPlacementAndKeepsTheme(string window)
    {
        _fileSystem.AddFile(_filePath, new MockFileData($$"""{ "Theme": "Light", "MainWindow": {{window}} }"""));

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
            $$"""{ "MainWindow": { "Left": 1, "Top": 2, "Width": 800, "Height": 600, "IsMaximized": {{value}} } }"""));

        var settings = await _store.LoadAsync(TestContext.Current.CancellationToken);

        settings.MainWindow.ShouldBe(new WindowPlacement(1, 2, 800, 600, false));
    }

    [Fact]
    public async Task LoadAsync_InvalidPanelHeights_ReturnsDefaultForEachInvalidHeight()
    {
        _fileSystem.AddFile(_filePath, new MockFileData(
            """{ "MainWindow": { "LogPanelHeight": "high", "SummaryPanelHeight": 5000 } }"""));

        var settings = await _store.LoadAsync(TestContext.Current.CancellationToken);

        settings.LogPanelHeight.ShouldBe(UiSettings.DefaultLogPanelHeight);
        settings.SummaryPanelHeight.ShouldBe(UiSettings.DefaultSummaryPanelHeight);
    }

    [Fact]
    public async Task LoadAsync_ByteOrderMarkAndComments_ReadsFile()
    {
        _fileSystem.AddFile(_filePath, new MockFileData(
            [0xEF, 0xBB, 0xBF, .. """{ /* edited */ "Theme": "Dark", }"""u8.ToArray()]));

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).Theme.ShouldBe(UiTheme.Dark);
    }

    [Fact]
    public async Task SaveAsync_NoFile_WritesRootObject()
    {
        await _store.SaveAsync(
            new(UiTheme.Dark, new(100, 50.5, 1000, 700, true), 160, 220), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
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
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Fact]
    public async Task SaveAsync_DefaultPlacementAndInvalidHeight_WritesHeightsOnlyWithDefault()
    {
        await _store.SaveAsync(UiSettings.Default with { SummaryPanelHeight = 1 }, TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Theme": "System",
              "MainWindow": {
                "LogPanelHeight": 160,
                "SummaryPanelHeight": 220
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Fact]
    public async Task SaveAsync_OtherSettingsFiles_LeavesThemUntouched()
    {
        const string appSettings = """{ "Tools": { "GitPath": "C:\\Git\\git.exe" } }""";
        const string legacy = """{ "Ui": { "Theme": "Light" } }""";
        _fileSystem.AddFile(_appSettingsPath, new MockFileData(appSettings));
        _fileSystem.AddFile(_legacyUserSettingsPath, new MockFileData(legacy));

        await _store.SaveAsync(UiSettings.Default with { Theme = UiTheme.Dark }, TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_appSettingsPath).ShouldBe(appSettings);
        _fileSystem.File.ReadAllText(_legacyUserSettingsPath).ShouldBe(legacy);
        _fileSystem.File.Exists(_appSettingsPath + AtomicFileWriter.BackupExtension).ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_FileExists_KeepsPreviousAsBackup()
    {
        const string previous = """{ "Theme": "Dark" }""";
        _fileSystem.AddFile(_filePath, new MockFileData(previous));

        await _store.SaveAsync(UiSettings.Default, TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath + AtomicFileWriter.BackupExtension).ShouldBe(previous);
        (await _store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBe(UiSettings.Default);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public async Task SaveAsync_EmptyFile_WritesFile(string content)
    {
        _fileSystem.AddFile(_filePath, new MockFileData(content));

        await _store.SaveAsync(UiSettings.Default with { Theme = UiTheme.Dark }, TestContext.Current.CancellationToken);

        (await _store.LoadAsync(TestContext.Current.CancellationToken)).Theme.ShouldBe(UiTheme.Dark);
    }

    [Theory]
    [InlineData("{ \"Theme\": \"Dark\"")]
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
        const string content = """{ "Theme": "Dark" }""";
        var locked = new MockFileData(content) { AllowedFileShare = FileShare.None };
        _fileSystem.AddFile(_filePath, locked);

        await Should.ThrowAsync<IOException>(() => _store.SaveAsync(UiSettings.Default, TestContext.Current.CancellationToken));

        locked.TextContents.ShouldBe(content);
        _fileSystem.File.Exists(_filePath + AtomicFileWriter.BackupExtension).ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_FileLocked_ReturnsDefault()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""{ "Theme": "Dark" }""") { AllowedFileShare = FileShare.None });

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
