using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Switches the theme in the settings editor and checks that the windows change at once and that the choice is
/// restored at the next start.
/// </summary>
public sealed class ThemeTests : IDisposable
{
    /// <summary>The mean brightness, 0 to 255, above which a window counts as light.</summary>
    private const double _lightThreshold = 160;

    /// <summary>The mean brightness, 0 to 255, below which a window counts as dark.</summary>
    private const double _darkThreshold = 100;

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private AppSession? _session;

    /// <summary>
    /// Creates the installation with one standalone repository.
    /// </summary>
    public ThemeTests()
    {
        _installation = new(_api.Url);
        _installation.NewBackupConfig().WithRepository("octo", "alpha", 1).WriteTo(_installation.BackupConfigPath);
    }

    [Fact]
    public void SelectTheme_LightThenDark_AppliesAtOnceAndSaves()
    {
        var session = Start();
        var themes = OpenSettings(session);

        themes.Select("Light");
        AppSession.WaitUntil(() => MeanBrightness(session.MainWindow) > _lightThreshold, "the light theme");
        // Switching the theme replaces the templates of the controls, so the list is looked up again.
        session.WaitFor("ThemeComboBox").AsComboBox().Select("Dark");

        AppSession.WaitUntil(() => MeanBrightness(session.MainWindow) < _darkThreshold, "the dark theme");
        AppSession.WaitUntil(() => SavedTheme() == "Dark", "the theme in user-settings.json");
    }

    [Fact]
    public void SavedDarkTheme_Start_OpensDarkWithDarkSelected()
    {
        _installation.WriteUserSettings("""{ "Ui": { "Theme": "dark" } }""");

        var session = Start();
        session.WaitFor("RepositoryTree");

        AppSession.WaitUntil(() => MeanBrightness(session.MainWindow) < _darkThreshold, "the dark theme");
        OpenSettings(session).SelectedItem.ShouldNotBeNull().Text.ShouldBe("Dark");
    }

    [Fact]
    public void Cancel_AfterThemeChange_ReturnsToTreeWithoutDiscardQuestion()
    {
        var session = Start();
        var themes = OpenSettings(session);
        themes.Select("Dark");
        AppSession.WaitUntil(() => SavedTheme() == "Dark", "the theme in user-settings.json");

        session.Invoke("CancelSettingsButton");

        session.WaitFor("RepositoryTree");
    }

    /// <summary>
    /// Stops the application, then deletes the installation and stops the API stub.
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
        _installation.Dispose();
        _api.Dispose();
    }

    /// <summary>
    /// Returns the mean brightness of the pixels of <paramref name="element"/> on the screen.
    /// </summary>
    /// <param name="element">The element, usually a window.</param>
    /// <returns>The mean of the red, green and blue values of every eighth pixel in both directions, from 0 to 255.</returns>
    private static double MeanBrightness(AutomationElement element)
    {
        using var image = Capture.Element(element);
        var bitmap = image.Bitmap;
        var sum = 0.0;
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 8)
        {
            for (var x = 0; x < bitmap.Width; x += 8)
            {
                var pixel = bitmap.GetPixel(x, y);
                sum += (pixel.R + pixel.G + pixel.B) / 3.0;
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }

    /// <summary>
    /// Opens the settings editor and returns its theme list.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>The theme list.</returns>
    private static ComboBox OpenSettings(AppSession session)
    {
        session.WaitFor("RepositoryTree");
        session.Invoke("SettingsButton");
        return session.WaitFor("ThemeComboBox").AsComboBox();
    }

    /// <summary>
    /// Returns the theme saved in <c>user-settings.json</c>, or <see langword="null"/> while there is none.
    /// </summary>
    /// <returns>The saved theme name.</returns>
    private string? SavedTheme()
    {
        if (!File.Exists(_installation.UserSettingsPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(_installation.UserSettingsPath));
        return document.RootElement.TryGetProperty("Ui", out var ui) && ui.TryGetProperty("Theme", out var theme)
            ? theme.GetString()
            : null;
    }

    /// <summary>
    /// Starts the application of the installation.
    /// </summary>
    /// <returns>The session, disposed with the test.</returns>
    private AppSession Start() => _session = AppSession.Start(_installation);
}
