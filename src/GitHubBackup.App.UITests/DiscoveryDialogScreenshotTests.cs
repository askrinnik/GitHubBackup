using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Takes screenshots of the discovery dialog with a New, a Renamed, a Missing and a Returned row, in the light and
/// the dark theme, and measures its table: one baseline per row, text clear of the cell edges, readable selection.
/// </summary>
/// <remarks>
/// The run that opens the dialog is not applied, so no git command runs. The tests need an unlocked interactive
/// desktop: Windows does not render windows while the workstation is locked, and a blank capture fails the test with a
/// message that says so.
/// </remarks>
public sealed class DiscoveryDialogScreenshotTests : IDisposable
{
    /// <summary>The height of the live log panel, which gives the display scale.</summary>
    private const double _logPanelHeight = 160;

    /// <summary>The kinds of the rows of the scenario, top to bottom.</summary>
    private static readonly string[] _kinds = ["New", "Renamed", "Missing", "Returned"];

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private AppSession? _session;

    /// <summary>
    /// Creates the installation with the discovery review scenario.
    /// </summary>
    public DiscoveryDialogScreenshotTests()
    {
        _installation = new(_api.Url);
        UiScenarios.DiscoveryReview.Prepare(_api, _installation.ArchivesPath, _installation.BackupConfigPath);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Dialog_FourKindsOfRows_AlignsTextOfEveryRow(string theme)
    {
        var shot = OpenDialog(theme);

        var rows = shot.Table.WholeRows();

        rows.Count.ShouldBe(_kinds.Length);
        foreach (var row in rows)
        {
            ScreenshotAssert.RowAligned(shot.Image, shot.Table, row, shot.Scale, $"{shot.Description}, {_kinds[row]} row");
        }
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Dialog_FourKindsOfRows_KeepsTextClearOfCellEdges(string theme)
    {
        var shot = OpenDialog(theme);

        foreach (var header in shot.Table.HeaderCells())
        {
            ScreenshotAssert.TextClear(shot.Image, header, shot.Scale, $"{shot.Description}: column header {header}", requireText: false);
        }

        foreach (var row in shot.Table.WholeRows())
        {
            ScreenshotAssert.RowClear(shot.Image, shot.Table, row, shot.Scale, $"{shot.Description}, {_kinds[row]} row");
        }
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Dialog_SelectedNewRow_KeepsAlignmentAndTextContrast(string theme)
    {
        var shot = OpenDialog(theme, selectFirstRow: true);

        ScreenshotAssert.RowAligned(shot.Image, shot.Table, 0, shot.Scale, $"{shot.Description}, selected row");
        foreach (var cell in shot.Table.Cells(0).Where(cell => LayoutMeasurement.TextBounds(shot.Image, cell, shot.Scale) is not null))
        {
            LayoutMeasurement.TextContrast(shot.Image, cell)
                .ShouldBeGreaterThanOrEqualTo(ScreenshotAssert.MinTextContrast, $"{shot.Description}: text of the selected row in {cell}");
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
    /// Starts the application in <paramref name="theme"/>, runs a backup of everything and captures the discovery
    /// dialog once its four rows are shown.
    /// </summary>
    /// <param name="theme">The theme name.</param>
    /// <param name="selectFirstRow">Whether to select the first row before the capture.</param>
    /// <returns>The capture and what the measurements need.</returns>
    private DialogShot OpenDialog(string theme, bool selectFirstRow = false)
    {
        _installation.WriteUiSettings(new UiSettingsJson { Theme = theme, LogPanelHeight = _logPanelHeight }.Build());
        var session = _session = AppSession.Start(_installation);
        session.WaitFor("RepositoryTree");
        var scale = session.WaitFor("RunLogPanel").BoundingRectangle.Height / _logPanelHeight;
        session.Invoke("BackupAllButton");
        var dialog = session.WaitForWindow("DiscoveryReviewWindow", UiTimeouts.Run);
        var table = AppSession.WaitFor(dialog, "DiscoveryTable");
        AppSession.WaitUntil(() => Rows(table).Length == _kinds.Length, "the four rows of the discovery table");
        foreach (var kind in _kinds)
        {
            table.FindFirstDescendant(condition => condition.ByName(kind)).ShouldNotBeNull($"the {kind} row");
        }

        if (selectFirstRow)
        {
            var selection = Rows(table)[0].Patterns.SelectionItem.Pattern;
            selection.Select();
            AppSession.WaitUntil(() => selection.IsSelected.Value, "the first row to be selected");
        }

        var description = $"discovery dialog, {theme} theme";
        var image = ScreenCapture.Window(dialog, $"the {description}");
        return new(image, scale, TableGeometry.Read(table, dialog.BoundingRectangle.Location), description);
    }

    /// <summary>
    /// Returns the rows of the discovery table.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <returns>The rows, top to bottom.</returns>
    private static AutomationElement[] Rows(AutomationElement table) =>
        table.FindAllChildren(condition => condition.ByControlType(ControlType.DataItem));

    /// <summary>
    /// Holds a capture of the discovery dialog and what the measurements need.
    /// </summary>
    /// <param name="Image">The capture.</param>
    /// <param name="Scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <param name="Table">The geometry of the discovery table.</param>
    /// <param name="Description">The window and the theme, for the messages.</param>
    private sealed record DialogShot(PixelImage Image, double Scale, TableGeometry Table, string Description);
}
