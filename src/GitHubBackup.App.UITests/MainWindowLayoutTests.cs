using System.Drawing;
using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using GitHubBackup.Core;
using GitHubBackup.Core.Ui;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Checks the layout of the main window: the cards and the splitters between the tree, the live log and the results, the columns of
/// the tree, and the placement and panel heights restored at the next start.
/// </summary>
public sealed class MainWindowLayoutTests : IDisposable
{
    /// <summary>The tolerance, in pixels, of bounds compared after rounding to device pixels.</summary>
    private const int _pixelTolerance = 3;

    /// <summary>The elements that are hidden until a run has a summary.</summary>
    private static readonly string[] _hiddenWithoutSummary = ["RunResultsPanel", "RunTotalsPanel", "LogSummarySplitter"];

    /// <summary>The automation ids of the four panels, top to bottom.</summary>
    private static readonly string[] _panelsTopToBottom = ["RepositoryTreePanel", "RunLogPanel", "RunResultsPanel", "RunTotalsPanel"];

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private AppSession? _session;

    /// <summary>
    /// Creates the installation with one account of two repositories and one standalone repository.
    /// </summary>
    public MainWindowLayoutTests()
    {
        _installation = new(_api.Url);
        _installation.NewBackupConfig()
            .WithAccount("acme", ("a", 1), ("a-repository-with-a-much-longer-name-than-the-others", 2))
            .WithRepository("octo", "mid-length-name", 3)
            .WriteTo(_installation.BackupConfigPath);
    }

    [Fact]
    public async Task DragTreeLogSplitter_Restart_RestoresLogPanelHeight()
    {
        var session = Start();
        session.WaitFor("RepositoryTree");
        var before = session.WaitFor("RunLogPanel").BoundingRectangle.Height;
        var scale = before / UiSettings.DefaultLogPanelHeight;

        DragVertically(session.WaitFor("TreeLogSplitter"), -(int)(100 * scale));

        AppSession.WaitUntil(
            () => session.WaitFor("RunLogPanel").BoundingRectangle.Height > before + (50 * scale), "the live log to grow");
        var resized = session.WaitFor("RunLogPanel").BoundingRectangle.Height;
        await CloseAsync(session);
        _installation.ReadMainWindowSettings().GetProperty("LogPanelHeight").GetDouble()
            .ShouldBe(resized / scale, _pixelTolerance);

        var next = Start();
        next.WaitFor("RepositoryTree");
        AppSession.WaitUntil(
            () => Math.Abs(next.WaitFor("RunLogPanel").BoundingRectangle.Height - resized) <= _pixelTolerance,
            "the live log to open at the saved height");
    }

    [Fact]
    public async Task DragLogSummarySplitter_AfterRun_ResizesSummaryAndSavesItsHeight()
    {
        _installation.NewBackupConfig().WriteTo(_installation.BackupConfigPath);
        var session = Start();
        session.WaitFor("EmptyTreeMessage");
        var hiddenSplitter = session.MainWindow.FindFirstDescendant(condition => condition.ByAutomationId("LogSummarySplitter"));
        (hiddenSplitter is null || hiddenSplitter.IsOffscreen).ShouldBeTrue();
        session.WaitFor("DryRunToggle").Patterns.Toggle.Pattern.Toggle();
        session.Invoke("BackupAllButton");
        session.WaitFor("RunSummaryTotals", UiTimeouts.Run);
        var log = session.WaitFor("RunLogPanel").BoundingRectangle.Height;
        var scale = log / UiSettings.DefaultLogPanelHeight;
        var before = SummaryHeight(session);

        DragVertically(session.WaitFor("LogSummarySplitter"), -(int)(60 * scale));

        AppSession.WaitUntil(() => SummaryHeight(session) >= before + (50 * scale), "the summary to grow");
        var resized = SummaryHeight(session);
        ((double)session.WaitFor("RunLogPanel").BoundingRectangle.Height).ShouldBe(log, _pixelTolerance);
        await CloseAsync(session);
        _installation.ReadMainWindowSettings().GetProperty("SummaryPanelHeight").GetDouble()
            .ShouldBeGreaterThan(UiSettings.DefaultSummaryPanelHeight + 40);
        resized.ShouldBeGreaterThan(before);
    }

    [Fact]
    public void SummaryWithTable_SmallSavedHeightAndDragDown_KeepsTableRowAndLinesWhole()
    {
        _api.GivenRepositoryUnavailable(3, "octo", "mid-length-name");
        _installation.NewBackupConfig().WithRepository("octo", "mid-length-name", 3).WriteTo(_installation.BackupConfigPath);
        _installation.WriteUiSettings("""{ "MainWindow": { "SummaryPanelHeight": 80 } }""");
        var session = Start();
        var tree = session.WaitFor("RepositoryTree");
        AppSession.WaitUntil(
            () => tree.FindFirstDescendant(condition => condition.ByName("octo/mid-length-name")) is not null,
            "the tree item of the repository");
        tree.FindFirstDescendant(condition => condition.ByName("octo/mid-length-name"))!.Patterns.SelectionItem.Pattern.Select();
        session.WaitFor("DryRunToggle").Patterns.Toggle.Pattern.Toggle();
        var backup = session.WaitFor("BackupRepositoryButton");
        AppSession.WaitUntil(() => backup.IsEnabled, "Backup repository to be enabled");

        AppSession.Invoke(backup);
        session.WaitFor("RunSummaryTotals", UiTimeouts.Run);
        var scale = session.WaitFor("RunLogPanel").BoundingRectangle.Height / UiSettings.DefaultLogPanelHeight;

        AppSession.WaitUntil(() => SummaryIsWhole(session, scale), "the summary to grow to its table row and lines");
        DragVertically(session.WaitFor("LogSummarySplitter"), (int)(300 * scale));
        AppSession.WaitUntil(() => SummaryIsWhole(session, scale), "the summary to stop at its table row and lines");
    }

    [Fact]
    public void Tree_NamesOfDifferentLength_AlignsStatusAndDateColumns()
    {
        var session = Start();
        var tree = session.WaitFor("RepositoryTree");
        AppSession.WaitUntil(() => tree.FindAllChildren().Length == 2, "the account and the group of standalone repositories");
        foreach (var node in tree.FindAllChildren())
        {
            node.Patterns.ExpandCollapse.Pattern.Expand();
        }

        AppSession.WaitUntil(() => FindAll(tree, "RepositoryStatusText").Length == 3, "three repository rows");

        FindAll(tree, "RepositoryStatusText").Select(text => text.BoundingRectangle.Left).Distinct().Count().ShouldBe(1);
        FindAll(tree, "RepositoryLastArchiveText").Select(text => text.BoundingRectangle.Left).Distinct().Count().ShouldBe(1);
        var longName = FindAll(tree, "RepositoryNameText").Max(text => text.BoundingRectangle.Right);
        FindAll(tree, "RepositoryStatusText")[0].BoundingRectangle.Left.ShouldBeGreaterThan(longName);
    }

    [Fact]
    public void Start_WithoutSummary_ShowsOnlyTreeAndLogPanels()
    {
        var session = Start();
        session.WaitFor("RepositoryTree");

        var tree = session.WaitFor("RepositoryTreePanel").BoundingRectangle;
        var log = session.WaitFor("RunLogPanel").BoundingRectangle;
        log.Top.ShouldBeGreaterThan(tree.Bottom);
        foreach (var id in _hiddenWithoutSummary)
        {
            var hidden = session.MainWindow.FindFirstDescendant(condition => condition.ByAutomationId(id));
            (hidden is null || hidden.IsOffscreen).ShouldBeTrue($"{id} is hidden without a summary");
        }
    }

    [Fact]
    public void DryRun_AfterRun_ShowsFourSeparatedPanelsWithTheirContent()
    {
        _api.GivenRepositoryUnavailable(3, "octo", "mid-length-name");
        _installation.NewBackupConfig().WithRepository("octo", "mid-length-name", 3).WriteTo(_installation.BackupConfigPath);
        var session = Start();

        RunDryRunOfUnavailableRepository(session);

        var panels = _panelsTopToBottom
            .Select(id => session.WaitFor(id).BoundingRectangle)
            .ToArray();
        for (var index = 1; index < panels.Length; index++)
        {
            (panels[index].Top - panels[index - 1].Bottom).ShouldBeGreaterThan(0, $"a gap above panel {index}");
            ((double)panels[index].Left).ShouldBe(panels[0].Left, _pixelTolerance);
            ((double)panels[index].Right).ShouldBe(panels[0].Right, _pixelTolerance);
        }

        var window = session.MainWindow.BoundingRectangle;
        panels[0].Left.ShouldBeGreaterThan(window.Left);
        panels[^1].Right.ShouldBeLessThan(window.Right);
        panels[^1].Bottom.ShouldBeLessThan(window.Bottom);
        Inside(session, "RunSummaryHeader", panels[2]);
        Inside(session, "RunSummaryTable", panels[2]);
        Inside(session, "RunSummaryTotals", panels[3]);
        Inside(session, "RunSummaryLogPath", panels[3]);
    }

    [Fact]
    public void Tree_AfterExpanding_ShowsAlignedIconBeforeEachNodeName()
    {
        var session = Start();
        var tree = session.WaitFor("RepositoryTree");
        AppSession.WaitUntil(() => tree.FindAllChildren().Length == 2, "the account and the group of standalone repositories");
        foreach (var node in tree.FindAllChildren())
        {
            node.Patterns.ExpandCollapse.Pattern.Expand();
        }

        AppSession.WaitUntil(() => FindAll(tree, "RepositoryNodeIcon").Length == 3, "three repository icons");

        var account = session.WaitFor("AccountNodeIcon").BoundingRectangle;
        var group = session.WaitFor("RepositoryGroupNodeIcon").BoundingRectangle;
        ((double)account.Left).ShouldBe(group.Left, _pixelTolerance);
        ((double)account.Width).ShouldBe(group.Width, _pixelTolerance);
        var icons = FindAll(tree, "RepositoryNodeIcon");
        icons.Select(icon => icon.BoundingRectangle.Left).Distinct().Count().ShouldBe(1);
        icons[0].BoundingRectangle.Left.ShouldBeGreaterThan(account.Left);
        var names = FindAll(tree, "RepositoryNameText");
        for (var index = 0; index < icons.Length; index++)
        {
            icons[index].BoundingRectangle.Right.ShouldBeLessThanOrEqualTo(names[index].BoundingRectangle.Left + _pixelTolerance);
        }
    }

    [Fact]
    public async Task OldUiSettingsFile_Start_RestoresLogAndResultsHeights()
    {
        _installation.NewBackupConfig().WriteTo(_installation.BackupConfigPath);
        _installation.WriteUiSettings("""{ "MainWindow": { "LogPanelHeight": 200, "SummaryPanelHeight": 300 } }""");
        var session = Start();
        session.WaitFor("EmptyTreeMessage");
        var scale = session.WaitFor("RunLogPanel").BoundingRectangle.Height / 200.0;
        session.WaitFor("DryRunToggle").Patterns.Toggle.Pattern.Toggle();
        session.Invoke("BackupAllButton");
        session.WaitFor("RunSummaryTotals", UiTimeouts.Run);

        ((double)SummaryHeight(session)).ShouldBe(300 * scale, _pixelTolerance);
        await CloseAsync(session);
        _installation.ReadMainWindowSettings().GetProperty("SummaryPanelHeight").GetDouble().ShouldBe(300, 1);
    }

    [Fact]
    public async Task SavedPlacement_Start_OpensAtSavedBoundsAndSavesThemAgain()
    {
        _installation.WriteUiSettings(MainWindowSettings(150, 120, 900, 640, isMaximized: false));
        var appSettings = File.ReadAllText(_installation.AppSettingsPath);

        var session = Start();
        session.WaitFor("RepositoryTree");

        var bounds = session.MainWindow.BoundingRectangle;
        var scale = bounds.Width / 900.0;
        ((double)bounds.Height).ShouldBe(640 * scale, _pixelTolerance);
        ((double)bounds.Left).ShouldBe(150 * scale, _pixelTolerance);
        ((double)bounds.Top).ShouldBe(120 * scale, _pixelTolerance);
        await CloseAsync(session);
        var saved = _installation.ReadMainWindowSettings();
        saved.GetProperty("Left").GetDouble().ShouldBe(150, 1);
        saved.GetProperty("Top").GetDouble().ShouldBe(120, 1);
        saved.GetProperty("Width").GetDouble().ShouldBe(900, 1);
        saved.GetProperty("Height").GetDouble().ShouldBe(640, 1);
        saved.GetProperty("IsMaximized").GetBoolean().ShouldBeFalse();
        File.ReadAllText(_installation.AppSettingsPath).ShouldBe(appSettings);
    }

    [Fact]
    public void SavedPlacementOffScreen_Start_OpensOnScreenInNormalState()
    {
        _installation.WriteUiSettings(MainWindowSettings(-50000, -50000, 900, 640, isMaximized: false));

        var session = Start();
        session.WaitFor("RepositoryTree");

        var bounds = session.MainWindow.BoundingRectangle;
        Monitors.Overlap(bounds).ShouldBeTrue($"the window bounds {bounds} overlap no connected monitor"); session.MainWindow.Patterns.Window.Pattern.WindowVisualState.Value.ShouldBe(WindowVisualState.Normal);
    }

    [Fact]
    public void SavedPlacementOffScreenMaximized_Start_OpensMaximized()
    {
        _installation.WriteUiSettings(MainWindowSettings(-50000, -50000, 900, 640, isMaximized: true));

        var session = Start();
        session.WaitFor("RepositoryTree");

        session.MainWindow.Patterns.Window.Pattern.WindowVisualState.Value.ShouldBe(WindowVisualState.Maximized);
    }

    [Fact]
    public async Task ClosedMaximized_Restart_KeepsRestoreBoundsAndOpensMaximized()
    {
        _installation.WriteUiSettings(MainWindowSettings(150, 120, 900, 640, isMaximized: true));
        var session = Start();
        session.WaitFor("RepositoryTree");
        session.MainWindow.Patterns.Window.Pattern.WindowVisualState.Value.ShouldBe(WindowVisualState.Maximized);

        await CloseAsync(session);

        var saved = _installation.ReadMainWindowSettings();
        saved.GetProperty("IsMaximized").GetBoolean().ShouldBeTrue();
        saved.GetProperty("Width").GetDouble().ShouldBe(900, 1);
        saved.GetProperty("Height").GetDouble().ShouldBe(640, 1);
        var next = Start();
        next.WaitFor("RepositoryTree");
        next.MainWindow.Patterns.Window.Pattern.WindowVisualState.Value.ShouldBe(WindowVisualState.Maximized);
    }

    [Fact]
    public async Task ClosedMinimized_Restart_SavesNormalBoundsAndOpensNormal()
    {
        _installation.WriteUiSettings(MainWindowSettings(150, 120, 900, 640, isMaximized: false));
        var session = Start();
        session.WaitFor("RepositoryTree");
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Minimized);
        AppSession.WaitUntil(
            () => session.MainWindow.Patterns.Window.Pattern.WindowVisualState.Value == WindowVisualState.Minimized,
            "the window to minimize");

        await CloseAsync(session);

        var saved = _installation.ReadMainWindowSettings();
        saved.GetProperty("IsMaximized").GetBoolean().ShouldBeFalse();
        saved.GetProperty("Left").GetDouble().ShouldBe(150, 1);
        saved.GetProperty("Width").GetDouble().ShouldBe(900, 1);
        var next = Start();
        next.WaitFor("RepositoryTree");
        next.MainWindow.Patterns.Window.Pattern.WindowVisualState.Value.ShouldBe(WindowVisualState.Normal);
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
    /// Returns the <c>ui-settings.json</c> text with a placement of the main window and the default panel heights.
    /// </summary>
    /// <param name="left">The left edge.</param>
    /// <param name="top">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="isMaximized">Whether the window is maximized.</param>
    /// <returns>The JSON text.</returns>
    private static string MainWindowSettings(double left, double top, double width, double height, bool isMaximized) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            { "MainWindow": { "Left": {{left}}, "Top": {{top}}, "Width": {{width}}, "Height": {{height}}, "IsMaximized": {{(isMaximized ? "true" : "false")}} } }
            """);

    /// <summary>
    /// Returns every descendant of <paramref name="root"/> with <paramref name="automationId"/>.
    /// </summary>
    /// <param name="root">The element to search.</param>
    /// <param name="automationId">The automation id.</param>
    /// <returns>The elements in tree order.</returns>
    private static AutomationElement[] FindAll(AutomationElement root, string automationId) =>
        root.FindAllDescendants(condition => condition.ByAutomationId(automationId));

    /// <summary>
    /// Measures the results panel, the card that the log and results splitter resizes.
    /// </summary>
    /// <param name="session">The running application.</param>
    /// <returns>The height in pixels.</returns>
    private static double SummaryHeight(AppSession session) => session.WaitFor("RunResultsPanel").BoundingRectangle.Height;

    /// <summary>
    /// Returns whether the results panel shows the header and the table with its column header and
    /// one row inside the visible part of its scroll area.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="scale">The device pixels per device-independent pixel.</param>
    /// <returns><see langword="true"/> when nothing of the results is cut.</returns>
    private static bool SummaryIsWhole(AppSession session, double scale)
    {
        var visible = session.WaitFor("RunResultsScroll").BoundingRectangle;
        var header = session.WaitFor("RunSummaryHeader").BoundingRectangle;
        var table = session.WaitFor("RunSummaryTable").BoundingRectangle;
        return table.Height >= (56 * scale) - _pixelTolerance
            && table.Top > header.Bottom
            && table.Bottom <= visible.Bottom + _pixelTolerance;
    }

    /// <summary>
    /// Runs a dry run of the repository that the API stub reports as unavailable, so that the summary shows a table row.
    /// </summary>
    /// <param name="session">The running application.</param>
    private static void RunDryRunOfUnavailableRepository(AppSession session)
    {
        var tree = session.WaitFor("RepositoryTree");
        AppSession.WaitUntil(
            () => tree.FindFirstDescendant(condition => condition.ByName("octo/mid-length-name")) is not null,
            "the tree item of the repository");
        tree.FindFirstDescendant(condition => condition.ByName("octo/mid-length-name"))!.Patterns.SelectionItem.Pattern.Select();
        session.WaitFor("DryRunToggle").Patterns.Toggle.Pattern.Toggle();
        var backup = session.WaitFor("BackupRepositoryButton");
        AppSession.WaitUntil(() => backup.IsEnabled, "Backup repository to be enabled");
        AppSession.Invoke(backup);
        session.WaitFor("RunSummaryTotals", UiTimeouts.Run);
    }

    /// <summary>
    /// Asserts that the element with <paramref name="automationId"/> lies inside <paramref name="panel"/>.
    /// </summary>
    /// <param name="session">The running application.</param>
    /// <param name="automationId">The automation id of the element.</param>
    /// <param name="panel">The bounds of the panel.</param>
    private static void Inside(AppSession session, string automationId, Rectangle panel)
    {
        var bounds = session.WaitFor(automationId).BoundingRectangle;
        bounds.Top.ShouldBeGreaterThanOrEqualTo(panel.Top, $"{automationId} top");
        bounds.Bottom.ShouldBeLessThanOrEqualTo(panel.Bottom + _pixelTolerance, $"{automationId} bottom");
        bounds.Left.ShouldBeGreaterThanOrEqualTo(panel.Left, $"{automationId} left");
        bounds.Right.ShouldBeLessThanOrEqualTo(panel.Right + _pixelTolerance, $"{automationId} right");
    }

    /// <summary>
    /// Drags <paramref name="splitter"/> up or down with the mouse.
    /// </summary>
    /// <param name="splitter">The splitter.</param>
    /// <param name="offset">The vertical distance in pixels; negative moves up.</param>
    private static void DragVertically(AutomationElement splitter, int offset)
    {
        var bounds = splitter.BoundingRectangle;
        var start = new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
        Mouse.Drag(start, start with { Y = start.Y + offset });
    }

    /// <summary>
    /// Closes the main window of <paramref name="session"/> and waits until the application exits successfully.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>A task that completes when the application has exited.</returns>
    private static async Task CloseAsync(AppSession session)
    {
        session.CloseMainWindow();
        (await session.WaitForExitAsync()).ShouldBe((int)ExitCode.Success);
    }

    /// <summary>
    /// Starts the application of the installation, stopping the previous session first.
    /// </summary>
    /// <returns>The session, disposed with the test.</returns>
    private AppSession Start()
    {
        _session?.Dispose();
        return _session = AppSession.Start(_installation);
    }
}
