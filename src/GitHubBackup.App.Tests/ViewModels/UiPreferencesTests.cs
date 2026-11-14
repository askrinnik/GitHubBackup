using GitHubBackup.App.Shell;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Ui;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="UiPreferences"/>: reading the saved preferences, switching the theme and saving.
/// </summary>
public sealed class UiPreferencesTests
{
    private const int _loadFailedEventId = 14;
    private const int _saveFailedEventId = 15;

    private readonly IUiSettingsStore _store = Substitute.For<IUiSettingsStore>();
    private readonly IThemeService _themeService = Substitute.For<IThemeService>();
    private readonly FakeLogger<UiPreferences> _logger = new();
    private readonly UiPreferences _preferences;

    /// <summary>
    /// Creates the subject over a substitute store and theme service.
    /// </summary>
    public UiPreferencesTests() => _preferences = new(_store, _themeService, _logger);

    [Fact]
    public void Constructor_HoldsDefaults()
    {
        _preferences.Theme.ShouldBe(UiTheme.System);
        _preferences.MainWindowPlacement.ShouldBeNull();
        _preferences.LogPanelHeight.ShouldBe(UiSettings.DefaultLogPanelHeight);
        _preferences.SummaryPanelHeight.ShouldBe(UiSettings.DefaultSummaryPanelHeight);
    }

    [Fact]
    public async Task LoadAsync_SavedSettings_TakesValuesAndAppliesTheme()
    {
        var placement = new WindowPlacement(10, 20, 900, 600, true);
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(new UiSettings(UiTheme.Dark, placement, 200, 300));

        await _preferences.LoadAsync(TestContext.Current.CancellationToken);

        _preferences.Theme.ShouldBe(UiTheme.Dark);
        _preferences.MainWindowPlacement.ShouldBe(placement);
        _preferences.LogPanelHeight.ShouldBe(200);
        _preferences.SummaryPanelHeight.ShouldBe(300);
        _themeService.Received(1).Apply(UiTheme.Dark);
    }

    [Fact]
    public async Task LoadAsync_StoreFails_KeepsDefaultsAppliesSystemAndLogs()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("The file is locked."));

        await _preferences.LoadAsync(TestContext.Current.CancellationToken);

        _preferences.Theme.ShouldBe(UiTheme.System);
        _themeService.Received(1).Apply(UiTheme.System);
        var record = _logger.Collector.GetSnapshot().Single(entry => entry.Id.Id == _loadFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.Exception.ShouldBeOfType<IOException>();
    }

    [Fact]
    public async Task ChangeThemeAsync_NewTheme_AppliesAndSavesCurrentValues()
    {
        _preferences.LogPanelHeight = 250;

        await _preferences.ChangeThemeAsync(UiTheme.Light, TestContext.Current.CancellationToken);

        _preferences.Theme.ShouldBe(UiTheme.Light);
        _themeService.Received(1).Apply(UiTheme.Light);
        await _store.Received(1).SaveAsync(
            new UiSettings(UiTheme.Light, null, 250, UiSettings.DefaultSummaryPanelHeight), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeThemeAsync_SameTheme_NeitherAppliesNorSaves()
    {
        await _preferences.ChangeThemeAsync(UiTheme.System, TestContext.Current.CancellationToken);

        _themeService.DidNotReceiveWithAnyArgs().Apply(default);
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ChangeThemeAsync_FileRefusesWrite_KeepsAppliedThemeAndLogs()
    {
        _store.SaveAsync(Arg.Any<UiSettings>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("The user settings file is not a valid JSON object."));

        await _preferences.ChangeThemeAsync(UiTheme.Dark, TestContext.Current.CancellationToken);

        _preferences.Theme.ShouldBe(UiTheme.Dark);
        _themeService.Received(1).Apply(UiTheme.Dark);
        var record = _logger.Collector.GetSnapshot().Single(entry => entry.Id.Id == _saveFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.Exception.ShouldBeOfType<IOException>();
    }

    [Fact]
    public async Task SaveAsync_StoreFails_LogsWithoutThrowing()
    {
        _store.SaveAsync(Arg.Any<UiSettings>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UnauthorizedAccessException("Access denied."));

        await _preferences.SaveAsync(TestContext.Current.CancellationToken);

        var record = _logger.Collector.GetSnapshot().Single(entry => entry.Id.Id == _saveFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.Exception.ShouldBeOfType<UnauthorizedAccessException>();
    }
}
