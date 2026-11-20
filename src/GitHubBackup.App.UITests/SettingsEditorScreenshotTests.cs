using System.Drawing;
using FlaUI.Core.AutomationElements;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Takes screenshots of the settings editor, in the light and the dark theme, and measures the check boxes of the
/// repository tables: each box is drawn whole and sits centred under its column header.
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
}
