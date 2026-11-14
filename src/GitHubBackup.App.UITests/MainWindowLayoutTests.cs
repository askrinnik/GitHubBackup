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
/// Checks the layout of the main window: the splitters between the tree, the live log and the summary, the columns of
/// the tree, and the placement and panel heights restored at the next start.
/// </summary>
public sealed class MainWindowLayoutTests : IDisposable
{
    /// <summary>The tolerance, in pixels, of bounds compared after rounding to device pixels.</summary>
    private const int _pixelTolerance = 3;

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
        var before = session.WaitFor("RunLog").BoundingRectangle.Height;
        var scale = before / UiSettings.DefaultLogPanelHeight;

        DragVertically(session.WaitFor("TreeLogSplitter"), -(int)(100 * scale));

        AppSession.WaitUntil(
            () => session.WaitFor("RunLog").BoundingRectangle.Height > before + (50 * scale), "the live log to grow");
        var resized = session.WaitFor("RunLog").BoundingRectangle.Height;
        await CloseAsync(session);
        _installation.ReadMainWindowSettings().GetProperty("LogPanelHeight").GetDouble()
            .ShouldBe(resized / scale, _pixelTolerance);

        var next = Start();
        next.WaitFor("RepositoryTree");
        AppSession.WaitUntil(
            () => Math.Abs(next.WaitFor("RunLog").BoundingRectangle.Height - resized) <= _pixelTolerance,
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
        var log = session.WaitFor("RunLog").BoundingRectangle.Height;
        var scale = log / UiSettings.DefaultLogPanelHeight;
        var before = session.WaitFor("RunSummaryScroll").BoundingRectangle.Height;

        DragVertically(session.WaitFor("LogSummarySplitter"), -(int)(60 * scale));

        AppSession.WaitUntil(
            () => session.WaitFor("RunSummaryScroll").BoundingRectangle.Height >= before + (50 * scale), "the summary to grow");
        var resized = session.WaitFor("RunSummaryScroll").BoundingRectangle.Height;
        ((double)session.WaitFor("RunLog").BoundingRectangle.Height).ShouldBe(log, _pixelTolerance);
        await CloseAsync(session);
        _installation.ReadMainWindowSettings().GetProperty("SummaryPanelHeight").GetDouble()
            .ShouldBeGreaterThan(UiSettings.DefaultSummaryPanelHeight + 40);
        resized.ShouldBeGreaterThan(before);
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
    public async Task SavedPlacement_Start_OpensAtSavedBoundsAndSavesThemAgain()
    {
        _installation.WriteUserSettings(MainWindowSettings(150, 120, 900, 640, isMaximized: false));

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
    }

    [Fact]
    public void SavedPlacementOffScreen_Start_OpensOnScreenInNormalState()
    {
        _installation.WriteUserSettings(MainWindowSettings(-50000, -50000, 900, 640, isMaximized: false));

        var session = Start();
        session.WaitFor("RepositoryTree");

        var bounds = session.MainWindow.BoundingRectangle;
        bounds.Left.ShouldBeGreaterThan(-100);
        bounds.Top.ShouldBeGreaterThan(-100);
        session.MainWindow.Patterns.Window.Pattern.WindowVisualState.Value.ShouldBe(WindowVisualState.Normal);
    }

    [Fact]
    public void SavedPlacementOffScreenMaximized_Start_OpensMaximized()
    {
        _installation.WriteUserSettings(MainWindowSettings(-50000, -50000, 900, 640, isMaximized: true));

        var session = Start();
        session.WaitFor("RepositoryTree");

        session.MainWindow.Patterns.Window.Pattern.WindowVisualState.Value.ShouldBe(WindowVisualState.Maximized);
    }

    [Fact]
    public async Task ClosedMaximized_Restart_KeepsRestoreBoundsAndOpensMaximized()
    {
        _installation.WriteUserSettings(MainWindowSettings(150, 120, 900, 640, isMaximized: true));
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
        _installation.WriteUserSettings(MainWindowSettings(150, 120, 900, 640, isMaximized: false));
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
    /// Returns the <c>user-settings.json</c> text with a placement of the main window and the default panel heights.
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
            { "Ui": { "MainWindow": { "Left": {{left}}, "Top": {{top}}, "Width": {{width}}, "Height": {{height}}, "IsMaximized": {{(isMaximized ? "true" : "false")}} } } }
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
