using System.Drawing;
using FlaUI.Core.AutomationElements;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Takes screenshots of the main window after a dry run, in the light and the dark theme, and measures the results
/// panel: aligned table rows, no cut text at the smallest panel height, and cards and lines that stand out.
/// </summary>
/// <remarks>
/// The tests need an unlocked interactive desktop: Windows does not render windows while the workstation is locked,
/// and a blank capture fails the test with a message that says so.
/// </remarks>
public sealed class MainWindowScreenshotTests : IDisposable
{
    /// <summary>The width of the main window in device-independent pixels.</summary>
    private const double _windowWidth = 1000;

    /// <summary>
    /// The height of the main window in device-independent pixels, so that the window fits a 1024 by 768 screen with
    /// a taskbar.
    /// </summary>
    private const double _windowHeight = 720;

    /// <summary>The automation ids of the four cards of the main window.</summary>
    private static readonly string[] _cards = ["RepositoryTreePanel", "RunLogPanel", "RunResultsPanel", "RunTotalsPanel"];

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private AppSession? _session;

    /// <summary>
    /// Creates the installation; each test writes its scenario and its user interface settings.
    /// </summary>
    public MainWindowScreenshotTests() => _installation = new(_api.Url);

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void DryRun_OneRow_AlignsRowAndKeepsTextClear(string theme)
    {
        var shot = DryRun(UiScenarios.MissingRepository, theme, logPanelHeight: 160);

        var rows = shot.Table.WholeRows();

        rows.ShouldHaveSingleItem();
        ScreenshotAssert.RowAligned(shot.Image, shot.Table, rows[0], shot.Scale, shot.Description);
        ScreenshotAssert.RowClear(shot.Image, shot.Table, rows[0], shot.Scale, shot.Description);
        ScreenshotAssert.TextClear(shot.Image, shot.Inside("RunTotalsPanel"), shot.Scale, $"{shot.Description}: totals", requireText: true);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void DryRun_ManyRowsWithDiscoveryLines_AlignsEveryWholeRowAndKeepsTotalsInside(string theme)
    {
        var shot = DryRun(UiScenarios.ManyRows, theme, logPanelHeight: 160);

        var rows = shot.Table.WholeRows();

        rows.Count.ShouldBeGreaterThan(2);
        foreach (var row in rows)
        {
            ScreenshotAssert.RowAligned(shot.Image, shot.Table, row, shot.Scale, shot.Description);
            ScreenshotAssert.RowClear(shot.Image, shot.Table, row, shot.Scale, shot.Description);
        }

        AppSession.WaitFor(shot.Window, "RunSummaryDiscovery").IsOffscreen.ShouldBeFalse();
        // A card that the window cuts reports only its visible part, so its bottom edge then shows no border.
        LayoutMeasurement.BorderContrast(shot.Image, shot.Bounds("RunTotalsPanel"), shot.Scale)
            .ShouldBeGreaterThanOrEqualTo(ScreenshotAssert.MinBorderContrast, $"{shot.Description}: the totals card ends inside the window");
        ScreenshotAssert.TextClear(shot.Image, shot.Inside("RunTotalsPanel"), shot.Scale, $"{shot.Description}: totals", requireText: true);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void DryRun_SmallestResultsHeight_ShowsColumnHeaderAndWholeRow(string theme)
    {
        var shot = DryRun(UiScenarios.MissingRepository, theme, logPanelHeight: 160, summaryPanelHeight: 80);

        var headers = shot.Table.HeaderCells();
        var rows = shot.Table.WholeRows();

        foreach (var header in headers)
        {
            ScreenshotAssert.TextClear(shot.Image, header, shot.Scale, $"{shot.Description}: column header {header}", requireText: false);
        }

        headers.Count(header => LayoutMeasurement.VerticalClearance(shot.Image, header, shot.Scale) >= ScreenshotAssert.MinClearance)
            .ShouldBeGreaterThan(0, $"{shot.Description}: the column header shows no text");
        rows.ShouldNotBeEmpty($"{shot.Description}: no table row is visible");
        ScreenshotAssert.TextClear(
            shot.Image, shot.Table.Cells(rows[0])[0], shot.Scale, $"{shot.Description}: first row {shot.Table.Rows[rows[0]]}", requireText: true);
        ScreenshotAssert.RowClear(shot.Image, shot.Table, rows[0], shot.Scale, shot.Description);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void DryRun_AfterRun_CardBordersAndSeparatorStandOut(string theme)
    {
        var shot = DryRun(UiScenarios.MissingRepository, theme, logPanelHeight: 160);

        foreach (var card in _cards)
        {
            LayoutMeasurement.BorderContrast(shot.Image, shot.Bounds(card), shot.Scale)
                .ShouldBeGreaterThanOrEqualTo(ScreenshotAssert.MinBorderContrast, $"{shot.Description}: border of {card}");
        }

        LayoutMeasurement.LineContrast(shot.Image, shot.Bounds("RunSummarySeparator"), shot.Scale)
            .ShouldBeGreaterThanOrEqualTo(ScreenshotAssert.MinLineContrast, $"{shot.Description}: separator above the table");
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
    /// Returns the user interface settings of a test: the theme, a fixed window size and the panel heights.
    /// </summary>
    /// <param name="theme">The theme name.</param>
    /// <param name="logPanelHeight">The height of the live log panel.</param>
    /// <param name="summaryPanelHeight">The height of the results panel, or <see langword="null"/> for the default.</param>
    /// <returns>The settings.</returns>
    private static UiSettingsJson UiSettings(string theme, double logPanelHeight, double? summaryPanelHeight = null) =>
        new()
        {
            Theme = theme,
            WindowSize = (_windowWidth, _windowHeight),
            LogPanelHeight = logPanelHeight,
            SummaryPanelHeight = summaryPanelHeight,
        };

    /// <summary>
    /// Prepares <paramref name="scenario"/>, starts the application, runs a dry run of everything and captures the
    /// main window once the summary is shown.
    /// </summary>
    /// <param name="scenario">The scenario.</param>
    /// <param name="theme">The theme name.</param>
    /// <param name="logPanelHeight">The height of the live log panel, which also gives the display scale.</param>
    /// <param name="summaryPanelHeight">The saved height of the results panel, or <see langword="null"/> for the default.</param>
    /// <returns>The capture and what the measurements need.</returns>
    private MainWindowShot DryRun(UiScenario scenario, string theme, double logPanelHeight, double? summaryPanelHeight = null)
    {
        scenario.Prepare(_api, _installation.ArchivesPath, _installation.BackupConfigPath);
        _installation.WriteUiSettings(UiSettings(theme, logPanelHeight, summaryPanelHeight).Build());

        var session = _session = AppSession.Start(_installation);
        session.WaitFor("RepositoryTree");
        session.WaitFor("DryRunToggle").Patterns.Toggle.Pattern.Toggle();
        session.Invoke("BackupAllButton");
        session.WaitFor("RunSummaryTotals", UiTimeouts.Run);
        var table = session.WaitFor("RunSummaryTable");
        AppSession.WaitUntil(() => TableGeometry.Read(table, session.MainWindow.BoundingRectangle.Location).Rows.Count > 0, "a row of the result table");

        var scale = session.WaitFor("RunLogPanel").BoundingRectangle.Height / logPanelHeight;
        var description = $"{scenario.Name}, {theme} theme";
        var image = ScreenCapture.Window(session.MainWindow, $"the main window ({description})");
        return new(image, session.MainWindow, scale, TableGeometry.Read(table, session.MainWindow.BoundingRectangle.Location), description);
    }

    /// <summary>
    /// Holds a capture of the main window and what the measurements need.
    /// </summary>
    /// <param name="Image">The capture.</param>
    /// <param name="Window">The main window.</param>
    /// <param name="Scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <param name="Table">The geometry of the result table.</param>
    /// <param name="Description">The scenario and the theme, for the messages.</param>
    private sealed record MainWindowShot(PixelImage Image, AutomationElement Window, double Scale, TableGeometry Table, string Description)
    {
        /// <summary>
        /// Returns the bounds of an element of the main window in the capture.
        /// </summary>
        /// <param name="automationId">The automation id.</param>
        /// <returns>The bounds.</returns>
        public Rectangle Bounds(string automationId) => ScreenCapture.Bounds(AppSession.WaitFor(Window, automationId), Window);

        /// <summary>
        /// Returns the inside of a card, without its border.
        /// </summary>
        /// <param name="automationId">The automation id of the card.</param>
        /// <returns>The bounds of the inside.</returns>
        public Rectangle Inside(string automationId)
        {
            var border = (int)Math.Ceiling(Scale) + 1;
            return Rectangle.Inflate(Bounds(automationId), -border, -border);
        }
    }
}
