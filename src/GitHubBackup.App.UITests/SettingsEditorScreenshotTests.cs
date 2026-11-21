using System.Drawing;
using FlaUI.Core.AutomationElements;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Takes screenshots of the settings editor, in the light and the dark theme, and measures the check boxes of the
/// repository tables, each drawn whole and centred under its column header, and the level lists of the logging
/// section, each with its whole text on the line of its label.
/// </summary>
/// <remarks>
/// The tests need an unlocked interactive desktop: Windows does not render windows while the workstation is locked,
/// and a blank capture fails the test with a message that says so.
/// </remarks>
public sealed class SettingsEditorScreenshotTests : IDisposable
{
    /// <summary>The height of the live log panel, which gives the display scale.</summary>
    private const double _logPanelHeight = 160;

    /// <summary>The check box columns of a repository table, as they appear in the automation ids.</summary>
    private static readonly string[] _columns = ["Paused", "Unavailable"];

    /// <summary>The level lists of the logging section, in order.</summary>
    private static readonly string[] _logLevelComboBoxes =
        ["LogLevelFileComboBox", "LogLevelConsoleComboBox", "LogLevelLiveLogComboBox", "LogLevelOpenTelemetryComboBox"];

    /// <summary>
    /// The width, in device-independent pixels, of the drop-down arrow at the right of a combo box with its margins;
    /// text that reaches into it is cut.
    /// </summary>
    private const int _comboBoxArrowWidth = 32;

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private AppSession? _session;

    /// <summary>
    /// Creates the installation with paused and unavailable repositories, so that the tables hold checked and
    /// unchecked boxes.
    /// </summary>
    public SettingsEditorScreenshotTests()
    {
        _installation = new(_api.Url);
        UiScenarios.PausedEntries.Prepare(_api, _installation.ArchivesPath, _installation.BackupConfigPath);
    }

    [Theory]
    [InlineData("light", "SettingsSectionRepositories", "Repository")]
    [InlineData("dark", "SettingsSectionRepositories", "Repository")]
    [InlineData("light", "SettingsSectionAccounts", "AccountRepository")]
    [InlineData("dark", "SettingsSectionAccounts", "AccountRepository")]
    public void RepositoryTable_CheckBoxes_DrawsWholeBoxCentredUnderHeader(string theme, string section, string idPrefix)
    {
        _installation.WriteUiSettings(new UiSettingsJson { Theme = theme, WindowSize = (1000, 720), LogPanelHeight = _logPanelHeight }.Build());
        var session = _session = AppSession.Start(_installation);
        var scale = session.WaitFor("RunLogPanel").BoundingRectangle.Height / _logPanelHeight;
        session.Invoke("SettingsButton");
        var save = session.WaitFor("SaveSettingsButton");
        AppSession.WaitUntil(() => save.IsEnabled, "the settings to be read");
        session.Select(section);
        var window = session.MainWindow;
        var description = $"{section}, {theme} theme";
        var columns = _columns
            .Select(column => (Header: session.WaitFor($"{idPrefix}{column}Header"), Id: $"{idPrefix}{column}CheckBox"))
            .ToList();
        AppSession.WaitUntil(() => columns.All(column => Shown(window, column.Id).Count >= 3), "three rows of the table");
        var image = ScreenCapture.Window(window, $"the settings editor ({description})");
        var tolerance = (int)Math.Ceiling(scale);
        var margin = (int)Math.Ceiling(4 * scale);

        foreach (var (header, id) in columns)
        {
            var headerText = LayoutMeasurement.TextBounds(image, ScreenCapture.Bounds(header, window), scale)
                .ShouldNotBeNull($"{description}: the header {header.Name} shows no text");
            var headerCentre = headerText.Left + (headerText.Width / 2.0);
            foreach (var checkBox in Shown(window, id))
            {
                var bounds = ScreenCapture.Bounds(checkBox, window);
                var box = LayoutMeasurement.InkBounds(image, Rectangle.Inflate(bounds, margin, 0))
                    .ShouldNotBeNull($"{description}: the check box {id} at {bounds} shows no box");

                // The box is square; a control narrower than its template cuts the right edge of the box.
                Math.Abs(box.Width - box.Height).ShouldBeLessThanOrEqualTo(tolerance, $"{description}: the box of {id} at {box} is not whole");
                Math.Abs(box.Left + (box.Width / 2.0) - headerCentre)
                    .ShouldBeLessThanOrEqualTo(2 * tolerance, $"{description}: the box of {id} at {box} is not centred under its header");
            }
        }
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void LoggingSection_LevelLists_DrawsCardAndWholeTextLinedUpWithLabels(string theme)
    {
        _installation.WriteUiSettings(new UiSettingsJson { Theme = theme, WindowSize = (1000, 720), LogPanelHeight = _logPanelHeight }.Build());
        var session = _session = AppSession.Start(_installation);
        var scale = session.WaitFor("RunLogPanel").BoundingRectangle.Height / _logPanelHeight;
        session.Invoke("SettingsButton");
        var save = session.WaitFor("SaveSettingsButton");
        AppSession.WaitUntil(() => save.IsEnabled, "the settings to be read");
        session.Select("SettingsSectionLogging");
        var window = session.MainWindow;
        var card = session.WaitFor("SettingsLoggingCard");
        var comboBoxes = _logLevelComboBoxes.Select(id => session.WaitFor(id)).ToList();
        var notice = session.WaitFor("LoggingRestartNotice");
        var description = $"logging section, {theme} theme";
        var image = ScreenCapture.Window(window, $"the settings editor ({description})");
        image.Save(Path.Combine(ScreenshotFolder(), $"SettingsEditor-Logging-{theme}.png"));
        var cardBounds = ScreenCapture.Bounds(card, window);
        var tolerance = (int)Math.Ceiling(scale);
        var inset = (int)Math.Ceiling(4 * scale);
        var arrow = (int)Math.Ceiling(_comboBoxArrowWidth * scale);

        LayoutMeasurement.BorderContrast(image, cardBounds, scale)
            .ShouldBeGreaterThanOrEqualTo(ScreenshotAssert.MinBorderContrast, $"{description}: border of the card");
        ScreenshotAssert.TextClear(image, ScreenCapture.Bounds(notice, window), scale, $"{description}: notice", requireText: true);
        foreach (var comboBox in comboBoxes)
        {
            var bounds = ScreenCapture.Bounds(comboBox, window);
            cardBounds.Contains(bounds).ShouldBeTrue($"{description}: {comboBox.AutomationId} at {bounds} lies outside the card");
            var textRegion = Rectangle.FromLTRB(bounds.Left + inset, bounds.Top + 1, bounds.Right - arrow, bounds.Bottom - 1);
            var label = Rectangle.FromLTRB(cardBounds.Left + inset, bounds.Top, bounds.Left - 1, bounds.Bottom);
            var text = LayoutMeasurement.TextBounds(image, textRegion, scale)
                .ShouldNotBeNull($"{description}: {comboBox.AutomationId} shows no text");

            ScreenshotAssert.TextClear(image, textRegion, scale, $"{description}: {comboBox.AutomationId}", requireText: true);
            ScreenshotAssert.TextClear(image, label, scale, $"{description}: label of {comboBox.AutomationId}", requireText: true);
            text.Right.ShouldBeLessThan(textRegion.Right - 1, $"{description}: the text of {comboBox.AutomationId} runs under its arrow");
            LayoutMeasurement.AlignmentSpread(image, [label, textRegion], scale)
                .ShouldBeLessThanOrEqualTo(2 * tolerance, $"{description}: {comboBox.AutomationId} and its label are not on one line");
        }
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
    /// Returns the shown descendants of <paramref name="root"/> with <paramref name="automationId"/>.
    /// </summary>
    /// <param name="root">The element to search.</param>
    /// <param name="automationId">The automation id.</param>
    /// <returns>The elements in tree order.</returns>
    private static List<AutomationElement> Shown(AutomationElement root, string automationId) =>
        [.. root.FindAllDescendants(condition => condition.ByAutomationId(automationId)).Where(element => !element.IsOffscreen)];

    /// <summary>
    /// Returns the folder that keeps the measured screenshots for review: the folder named by
    /// <c>UI_TEST_SCREENSHOTS</c>, as on CI, or <c>Screenshots</c> next to the test assembly.
    /// </summary>
    /// <returns>The path of the folder, which exists.</returns>
    private static string ScreenshotFolder()
    {
        var folder = Environment.GetEnvironmentVariable("UI_TEST_SCREENSHOTS") is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Screenshots");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
