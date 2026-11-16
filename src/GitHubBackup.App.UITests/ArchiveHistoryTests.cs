using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using GitHubBackup.App.ViewModels;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Opens the Archives tab of the history screen over archive folders prepared on the disk and checks the repository
/// table, the archives of the selected repository, sorting by a column header and the confirmation of the cleanup.
/// </summary>
public sealed class ArchiveHistoryTests : IDisposable
{
    private const string _owner = "octo";

    private const string _commit = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    private const string _toolRecorded = "octo_tool_20261001-100000_a1b2c3d4e5.zip";

    private const string _toolGone = "octo_tool_20261002-100000_a1b2c3d4e5.zip";

    private const string _toolUnrecorded = "octo_tool_20261003-100000_abcdef0123.zip";

    private const string _appRecorded = "octo_app_20261001-120000_a1b2c3d4e5.zip";

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private AppSession? _session;

    /// <summary>
    /// Creates the installation with the account <c>octo</c> listing <c>tool</c> and <c>app</c>. <c>tool</c> records
    /// two archives, one of whose files is gone, and holds one archive file without a record and one unrelated file;
    /// <c>app</c> records one archive whose file is present.
    /// </summary>
    public ArchiveHistoryTests()
    {
        _installation = new(_api.Url);
        _installation.NewBackupConfig().WithAccount(_owner, ("tool", 1), ("app", 2)).WriteTo(_installation.BackupConfigPath);

        var tool = CreateArchiveFolder("tool", 1, (_toolRecorded, "2026-10-01T10:00:00+03:00"), (_toolGone, "2026-10-02T10:00:00+03:00"));
        File.WriteAllBytes(Path.Combine(tool, _toolRecorded), new byte[1024]);
        File.WriteAllBytes(Path.Combine(tool, _toolUnrecorded), new byte[1024]);
        File.WriteAllText(Path.Combine(tool, "notes.txt"), "not an archive");

        var app = CreateArchiveFolder("app", 2, (_appRecorded, "2026-10-01T12:00:00+03:00"));
        File.WriteAllBytes(Path.Combine(app, _appRecorded), new byte[100]);
    }

    [Fact]
    public void OpenArchives_RepositoryTable_ShowsCountersInConfigurationOrder()
    {
        var table = OpenArchives();

        RowTexts(table).ShouldBe(
        [
            ["octo/tool", "octo", "2", "2", "1", "1", "2.0 KB", "2026-10-02 10:00:00"],
            ["octo/app", "octo", "1", "1", "0", "0", "100 B", "2026-10-01 12:00:00"],
        ]);
    }

    [Fact]
    public void SelectRepository_ShowsArchivesIncludingFileWithoutRecord()
    {
        var table = OpenArchives();

        Rows(table)[0].Patterns.SelectionItem.Pattern.Select();

        var archives = _session!.WaitFor("ArchiveHistoryList");
        AppSession.WaitUntil(() => Rows(archives).Length == 3, "the three archives of octo/tool");
        RowTexts(archives).Select(cells => (cells[2], cells[3])).ShouldBe(
        [
            (_toolUnrecorded, "Not in state"),
            (_toolGone, "Missing"),
            (_toolRecorded, "Present"),
        ]);
    }

    [Fact]
    public void ClickOnDiskHeader_SortsRowsByCount()
    {
        var table = OpenArchives();

        AppSession.Invoke(table.FindFirstDescendant(condition =>
            condition.ByControlType(ControlType.HeaderItem).And(condition.ByName("On disk"))).ShouldNotBeNull());

        AppSession.WaitUntil(() => RowTexts(table)[0][0] == "octo/app", "the table sorted by On disk");
        RowTexts(table).Select(cells => cells[3]).ShouldBe(["1", "2"]);
    }

    [Fact]
    public void DeleteOldArchives_AnsweredNo_ShowsNumbersAndChangesNothing()
    {
        var tool = Path.Combine(_installation.ArchivesPath, _owner, "tool");
        var statePath = Path.Combine(tool, ".backup-state.json");
        var originalState = File.ReadAllText(statePath);
        var table = OpenArchives();
        Rows(table)[0].Patterns.SelectionItem.Pattern.Select();

        _session!.Invoke("DeleteOldArchivesButton");
        var messageBox = _session.WaitForMessageBox(ArchiveHistoryViewModel.CleanupConfirmTitle);
        var text = AppSession.MessageText(messageBox);
        AppSession.Answer(messageBox, MessageBoxButtonId.No);

        text.ShouldContain("octo/tool");
        text.ShouldContain("Files to move to the Recycle Bin: 1 (1.0 KB)");
        text.ShouldContain("Records without a file to remove from .backup-state.json: 0");
        AppSession.WaitUntil(
            () => _session.WaitFor("DeleteOldArchivesButton").IsEnabled,
            "the cleanup button to be enabled again after the confirmation");
        File.ReadAllText(statePath).ShouldBe(originalState);
        File.Exists(Path.Combine(tool, _toolRecorded)).ShouldBeTrue();
        File.Exists(Path.Combine(tool, _toolUnrecorded)).ShouldBeTrue();
        File.Exists(statePath + ".bak").ShouldBeFalse();
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
    /// Creates the archive folder of a repository of <c>octo</c> with a state file that records the given archives.
    /// </summary>
    /// <param name="name">The repository name.</param>
    /// <param name="id">The GitHub id of the repository.</param>
    /// <param name="archives">The file name and creation time of each recorded archive, oldest first.</param>
    /// <returns>The absolute archive folder.</returns>
    private string CreateArchiveFolder(string name, long id, params (string File, string CreatedAt)[] archives)
    {
        var folder = Path.Combine(_installation.ArchivesPath, _owner, name);
        Directory.CreateDirectory(folder);
        var records = archives.Select(archive =>
            $$"""
            { "file": "{{archive.File}}", "createdAt": "{{archive.CreatedAt}}", "branch": "main", "headCommit": "{{_commit}}", "sizeBytes": 1024, "refs": { "heads/main": "{{_commit}}" } }
            """);
        File.WriteAllText(
            Path.Combine(folder, ".backup-state.json"),
            $$"""
            {
              "schemaVersion": 1,
              "repositoryId": {{id}},
              "fullName": "{{_owner}}/{{name}}",
              "repository": { "archives": [ {{string.Join(",", records)}} ] },
              "wiki": { "archives": [] }
            }
            """);
        return folder;
    }

    /// <summary>
    /// Starts the application, opens the history screen and its Archives tab, and waits for both repositories.
    /// </summary>
    /// <returns>The repository table.</returns>
    private AutomationElement OpenArchives()
    {
        var session = _session = AppSession.Start(_installation);
        session.WaitFor("RepositoryTree");
        session.Invoke("HistoryButton");
        session.Select("ArchivesTab");
        var table = session.WaitFor("ArchiveRepositoryList");
        AppSession.WaitUntil(() => Rows(table).Length == 2, "the two repositories of the configuration");
        return table;
    }

    /// <summary>
    /// Returns the rows of a data grid.
    /// </summary>
    /// <param name="grid">The data grid.</param>
    /// <returns>The rows in display order.</returns>
    private static AutomationElement[] Rows(AutomationElement grid) =>
        grid.FindAllChildren(condition => condition.ByControlType(ControlType.DataItem));

    /// <summary>
    /// Returns the text of every cell of every row of a data grid.
    /// </summary>
    /// <param name="grid">The data grid.</param>
    /// <returns>The cell texts of each row in display order.</returns>
    private static string[][] RowTexts(AutomationElement grid) =>
    [
        .. Rows(grid).Select(row => row.FindAllChildren()
            .Where(cell => cell.Patterns.GridItem.IsSupported)
            .Select(CellText)
            .ToArray()),
    ];

    /// <summary>
    /// Returns the text a data grid cell shows.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The text, empty when the cell shows none.</returns>
    private static string CellText(AutomationElement cell) =>
        cell.FindFirstDescendant(condition => condition.ByControlType(ControlType.Text))?.Name ?? string.Empty;
}
