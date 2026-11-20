using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Checks the live log of the main window: it shows the log of the whole session from the start of the application,
/// and the Clear log button empties it while later entries keep coming, also after the settings editor was open.
/// </summary>
public sealed class RunLogTests : IDisposable
{
    /// <summary>The start of the entry the application writes when it opens the run history at start-up.</summary>
    private const string _startupEntry = "The run history database";

    /// <summary>The start of the entry a dry run writes first.</summary>
    private const string _dryRunEntry = "Dry run started";

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private AppSession? _session;

    /// <summary>
    /// Creates the installation over the scenario of one repository that is missing on GitHub, so a dry run needs
    /// neither git nor 7-Zip.
    /// </summary>
    public RunLogTests()
    {
        _installation = new(_api.Url);
        UiScenarios.MissingRepository.Prepare(_api, _installation.ArchivesPath, _installation.BackupConfigPath);
    }

    [Fact]
    public void Start_BeforeAnyRun_ShowsEntriesOfApplicationStart()
    {
        var session = _session = AppSession.Start(_installation);
        var log = session.WaitFor("RunLog");

        AppSession.WaitUntil(() => Messages(log).Any(message => message.Contains(_startupEntry, StringComparison.Ordinal)), "the start-up entry in the live log");
    }

    [Fact]
    public void ClearRunLogButton_ThenDryRun_EmptiesLogAndShowsNewEntries()
    {
        var session = _session = AppSession.Start(_installation);
        var log = session.WaitFor("RunLog");
        AppSession.WaitUntil(() => Messages(log).Count > 0, "an entry in the live log");
        var button = session.WaitFor("ClearRunLogButton");

        button.Name.ShouldBe("Clear log");
        button.HelpText.ShouldBe("Clear log");
        AppSession.Invoke(button);
        AppSession.WaitUntil(() => Messages(log).Count == 0, "an empty live log");
        DryRun(session);

        AppSession.WaitUntil(() => Messages(log).Any(message => message.Contains(_dryRunEntry, StringComparison.Ordinal)), "the dry-run entry after clearing");
    }

    [Fact]
    public void ClearRunLogButton_SettingsOpenedAndClosed_ClearedEntriesStayGone()
    {
        var session = _session = AppSession.Start(_installation);
        DryRun(session);
        AppSession.WaitUntil(() => Messages(session.WaitFor("RunLog")).Any(message => message.Contains(_dryRunEntry, StringComparison.Ordinal)), "the dry-run entry");

        session.Invoke("ClearRunLogButton");
        AppSession.WaitUntil(() => Messages(session.WaitFor("RunLog")).Count == 0, "an empty live log");
        session.Invoke("SettingsButton");
        var save = session.WaitFor("SaveSettingsButton");
        AppSession.WaitUntil(() => save.IsEnabled, "the settings to be read");
        session.Invoke("CancelSettingsButton");
        session.WaitFor("RepositoryTree");
        session.WaitUntilGone("CancelSettingsButton");
        var log = session.WaitFor("RunLog");
        AppSession.WaitUntil(() => Messages(log).Any(message => message.Contains("Loaded the backup configuration", StringComparison.Ordinal)), "the entry of the tree read again");

        Messages(log).ShouldNotContain(message => message.Contains(_dryRunEntry, StringComparison.Ordinal));
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
    /// Returns the texts of the entries the live log shows.
    /// </summary>
    /// <remarks>
    /// The list virtualizes its rows, so only the realized rows are read; every check looks for an entry near the end,
    /// where the list is scrolled to.
    /// </remarks>
    /// <param name="log">The live log.</param>
    /// <returns>The texts of the rows, each the timestamp, the level and the message.</returns>
    private static List<string> Messages(AutomationElement log) =>
        [.. log.FindAllChildren(condition => condition.ByControlType(ControlType.ListItem))
            .Select(item => string.Join(' ', item.FindAllDescendants(condition => condition.ByControlType(ControlType.Text)).Select(text => text.Name)))];

    /// <summary>
    /// Runs a dry run of everything and waits for its summary.
    /// </summary>
    /// <param name="session">The running application.</param>
    private static void DryRun(AppSession session)
    {
        session.WaitFor("RepositoryTree");
        session.WaitFor("DryRunToggle").Patterns.Toggle.Pattern.Toggle();
        session.Invoke("BackupAllButton");
        session.WaitFor("RunSummaryTotals", UiTimeouts.Run);
    }
}
