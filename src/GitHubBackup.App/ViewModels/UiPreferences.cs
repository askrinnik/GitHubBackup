using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.App.Hosting;
using GitHubBackup.App.Shell;
using GitHubBackup.Core.Ui;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Holds the preferences of the user interface for the whole session: the theme, the placement of the main window
/// and the heights of its panels, which the views bind to and the application saves.
/// </summary>
/// <remarks>
/// One instance lives as long as the application, so the panel heights survive the screens that come and go in the
/// main window. Neither reading nor writing ever fails the caller: a failure is logged and the session goes on with
/// the values it has.
/// </remarks>
/// <param name="store">Reads and writes the preferences.</param>
/// <param name="themeService">Applies the theme to the windows.</param>
/// <param name="logger">Logs failures of reading and writing.</param>
public sealed partial class UiPreferences(
    IUiSettingsStore store,
    IThemeService themeService,
    ILogger<UiPreferences> logger) : ObservableObject
{
    /// <summary>The colour mode of the windows.</summary>
    [ObservableProperty]
    private UiTheme _theme = UiSettings.Default.Theme;

    /// <summary>The height of the live log panel of the main window.</summary>
    [ObservableProperty]
    private double _logPanelHeight = UiSettings.DefaultLogPanelHeight;

    /// <summary>The height of the run summary panel of the main window.</summary>
    [ObservableProperty]
    private double _summaryPanelHeight = UiSettings.DefaultSummaryPanelHeight;

    /// <summary>
    /// Gets or sets the placement of the main window: the saved one until the window records where it closes, or
    /// <see langword="null"/> for the default placement.
    /// </summary>
    public WindowPlacement? MainWindowPlacement { get; set; }

    /// <summary>
    /// Reads the saved preferences and applies the saved theme; the defaults stay when they cannot be read.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the preferences are read and the theme is applied.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = UiSettings.Default;
        try
        {
            settings = await store.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.UiSettingsLoadFailed(logger, exception);
        }

        Theme = settings.Theme;
        MainWindowPlacement = settings.MainWindow;
        LogPanelHeight = settings.LogPanelHeight;
        SummaryPanelHeight = settings.SummaryPanelHeight;
        themeService.Apply(settings.Theme);
    }

    /// <summary>
    /// Applies <paramref name="theme"/> at once and saves the preferences.
    /// </summary>
    /// <param name="theme">The new colour mode.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the preferences are saved or the failure is logged.</returns>
    public Task ChangeThemeAsync(UiTheme theme, CancellationToken cancellationToken)
    {
        if (theme == Theme)
        {
            return Task.CompletedTask;
        }

        Theme = theme;
        themeService.Apply(theme);
        return SaveAsync(cancellationToken);
    }

    /// <summary>
    /// Writes the current preferences; a failure is logged, never thrown, so it cannot keep the application from
    /// closing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the preferences are saved or the failure is logged.</returns>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.SaveAsync(
                new UiSettings(Theme, MainWindowPlacement, LogPanelHeight, SummaryPanelHeight), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.UiSettingsSaveFailed(logger, exception);
        }
    }
}
