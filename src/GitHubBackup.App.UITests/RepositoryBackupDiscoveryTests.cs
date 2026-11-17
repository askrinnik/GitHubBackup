using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Runs "Backup repository" for a repository that GitHub no longer reports and checks the discovery dialog and the
/// tree after the run.
/// </summary>
/// <remarks>
/// The API answers 404 for the repository by id and by name, so the single-repository check finds it missing without
/// any clone or archive.
/// </remarks>
public sealed class RepositoryBackupDiscoveryTests : IDisposable
{
    private const string _owner = "octo";
    private const string _name = "gone";
    private const long _id = 1;
    private const string _fullName = _owner + "/" + _name;

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private readonly AppSession _session;

    /// <summary>
    /// Writes a configuration that tracks the repository, makes the API answer 404 for it and starts the application.
    /// </summary>
    public RepositoryBackupDiscoveryTests()
    {
        _installation = new(_api.Url);
        _api.GivenRepositoryUnavailable(_id, _owner, _name);
        _installation.NewBackupConfig().WithRepository(_owner, _name, _id).WriteTo(_installation.BackupConfigPath);
        _session = AppSession.Start(_installation);
    }

    [Fact]
    public void BackupRepository_RepositoryGone_ShowsMissingRowThenUnavailableInTree()
    {
        var tree = _session.WaitFor("RepositoryTree");
        AppSession.WaitUntil(() => FindItem(tree) is not null, $"the tree item of {_fullName}");
        FindItem(tree)!.Patterns.SelectionItem.Pattern.Select();
        var backup = _session.WaitFor("BackupRepositoryButton");
        AppSession.WaitUntil(() => backup.IsEnabled, "Backup repository to be enabled");

        AppSession.Invoke(backup);
        var dialog = _session.WaitForWindow("DiscoveryReviewWindow", UiTimeouts.Run);

        var table = AppSession.WaitFor(dialog, "DiscoveryTable");
        AppSession.WaitUntil(
            () => table.FindFirstDescendant(condition => condition.ByName("Missing")) is not null,
            "the Missing row");
        AppSession.Invoke(AppSession.WaitFor(dialog, "ApplyDiscoveryButton"));

        var totals = _session.WaitFor("RunSummaryTotals", UiTimeouts.Run);
        totals.Name.ShouldBe("Archived: 0, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 1");
        AppSession.WaitUntil(
            () => tree.FindFirstDescendant(condition => condition.ByName("Unavailable")) is not null,
            "the Unavailable status in the tree");
    }

    /// <summary>
    /// Stops the application, then deletes the installation and stops the API stub.
    /// </summary>
    public void Dispose()
    {
        _session.Dispose();
        _installation.Dispose();
        _api.Dispose();
    }

    /// <summary>
    /// Finds the tree item of the tracked repository.
    /// </summary>
    /// <param name="tree">The repository tree.</param>
    /// <returns>The item, or <see langword="null"/> when the tree does not show it yet.</returns>
    private static AutomationElement? FindItem(AutomationElement tree) =>
        tree.FindFirstDescendant(condition =>
            condition.ByControlType(ControlType.TreeItem).And(condition.ByName(_fullName)));
}
