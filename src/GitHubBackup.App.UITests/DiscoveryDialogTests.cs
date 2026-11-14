using System.Text.Json;
using FlaUI.Core.AutomationElements;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Runs a backup from the main window that finds a repository the token can access but the configuration does not
/// track, and checks the discovery dialog and what each choice changes.
/// </summary>
/// <remarks>
/// The configuration tracks no repository and lets discovery offer repositories of other owners, so the run clones
/// only the repository the dialog adds; git and 7-Zip run for real against a local bare repository.
/// </remarks>
public sealed class DiscoveryDialogTests : IDisposable
{
    private const string _owner = "other";
    private const string _name = "fresh";
    private const long _id = 7;
    private const string _fullName = _owner + "/" + _name;

    private readonly LocalGitSandbox _sandbox = new();
    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private readonly string _originalConfig;
    private readonly AppSession _session;

    /// <summary>
    /// Serves the new repository through the API, writes the configuration and starts the application.
    /// </summary>
    public DiscoveryDialogTests()
    {
        _installation = new(_api.Url, Path.Combine(_sandbox.RootPath, "remotes"));
        _api.GivenCurrentUserRepositories(_api.GivenRepository(_id, _owner, _name));
        _installation.NewBackupConfig().WithDiscoverAccessibleRepositories().WriteTo(_installation.BackupConfigPath);
        _originalConfig = _installation.ReadBackupConfig();
        _session = AppSession.Start(_installation);
    }

    /// <summary>Gets the archive root of the installation as a JSON string.</summary>
    private string BackupRootJson => JsonSerializer.Serialize(_installation.ArchivesPath);

    [Fact]
    public void BackupAll_NewAccessibleRepository_ShowsDiscoveryDialogWithRow()
    {
        var dialog = OpenDialog();

        var table = AppSession.WaitFor(dialog, "DiscoveryTable");
        AppSession.WaitUntil(
            () => table.FindFirstDescendant(condition => condition.ByName(_fullName)) is not null,
            $"the row of {_fullName}");
        table.FindFirstDescendant(condition => condition.ByName("New")).ShouldNotBeNull();
        AppSession.WaitFor(dialog, "NewRepositoryPath").Patterns.Value.Pattern.Value.Value
            .ShouldBe(Path.Combine(_installation.ArchivesPath, _owner, _name));
    }

    [Fact]
    public void Apply_ExcludeAll_WritesExclusionAndShowsSummary()
    {
        var dialog = OpenDialog();

        AppSession.Invoke(AppSession.WaitFor(dialog, "ExcludeAllButton"));
        AppSession.Invoke(AppSession.WaitFor(dialog, "ApplyDiscoveryButton"));

        var totals = _session.WaitFor("RunSummaryTotals", UiTimeouts.Run);
        totals.Name.ShouldBe("Archived: 0, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 0");
        ReadConfig().ShouldBe(
            $$"""
            {
              "schemaVersion": 1,
              "backupRoot": {{BackupRootJson}},
              "sourcesRoot": null,
              "exclude": [
                "other/fresh"
              ],
              "discoverAccessibleRepositories": true,
              "accounts": [],
              "repositories": []
            }
            """.ReplaceLineEndings());
    }

    [Fact]
    public void Apply_SkipAll_LeavesConfigUnchangedAndReportsSkipped()
    {
        var dialog = OpenDialog();

        AppSession.Invoke(AppSession.WaitFor(dialog, "SkipAllButton"));
        AppSession.Invoke(AppSession.WaitFor(dialog, "ApplyDiscoveryButton"));

        var totals = _session.WaitFor("RunSummaryTotals", UiTimeouts.Run);
        totals.Name.ShouldBe("Archived: 0, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 1");
        _installation.ReadBackupConfig().ShouldBe(_originalConfig);
    }

    [Fact]
    public async Task Apply_AddAll_AddsRepositoryAndCreatesArchive()
    {
        await _sandbox.CreateRemoteAsync(_fullName, TestContext.Current.CancellationToken);
        var dialog = OpenDialog();

        AppSession.Invoke(AppSession.WaitFor(dialog, "AddAllButton"));
        AppSession.Invoke(AppSession.WaitFor(dialog, "ApplyDiscoveryButton"));

        var totals = _session.WaitFor("RunSummaryTotals", UiTimeouts.Run);
        totals.Name.ShouldBe("Archived: 1, UpToDate: 0, Warning: 0, Failed: 0, Skipped: 0");
        ReadConfig().ShouldBe(
            $$"""
            {
              "schemaVersion": 1,
              "backupRoot": {{BackupRootJson}},
              "sourcesRoot": null,
              "exclude": [],
              "discoverAccessibleRepositories": true,
              "accounts": [],
              "repositories": [
                {
                  "url": "https://github.com/other/fresh",
                  "id": 7,
                  "branch": null,
                  "path": null,
                  "status": "Active"
                }
              ]
            }
            """.ReplaceLineEndings());
        _installation.FindArchives(_owner, _name).ShouldHaveSingleItem();
    }

    [Fact]
    public void Cancel_DiscoveryDialog_CancelsRunAndLeavesConfigUnchanged()
    {
        var dialog = OpenDialog();

        AppSession.Invoke(AppSession.WaitFor(dialog, "CancelDiscoveryButton"));

        _session.WaitFor("RunSummaryCancelled", UiTimeouts.Run).Name
            .ShouldBe("Cancelled: the run was stopped before it finished.");
        _installation.ReadBackupConfig().ShouldBe(_originalConfig);
    }

    [Fact]
    public void Apply_InvalidArchiveFolder_ApplyButtonDisabled()
    {
        var dialog = OpenDialog();
        var apply = AppSession.WaitFor(dialog, "ApplyDiscoveryButton");
        AppSession.WaitUntil(() => apply.IsEnabled, "Apply to be enabled");

        AppSession.SetText(AppSession.WaitFor(dialog, "NewRepositoryPath"), @"relative\folder");

        AppSession.WaitUntil(() => !apply.IsEnabled, "Apply to be disabled");
    }

    /// <summary>
    /// Stops the application, then deletes the installation and the remotes and stops the API stub.
    /// </summary>
    public void Dispose()
    {
        _session.Dispose();
        _installation.Dispose();
        _api.Dispose();
        _sandbox.Dispose();
    }

    /// <summary>
    /// Reads <c>backup-config.json</c> with the line endings of the platform and without the final line break.
    /// </summary>
    /// <returns>The text of the file.</returns>
    private string ReadConfig() => _installation.ReadBackupConfig().ReplaceLineEndings().TrimEnd();

    /// <summary>
    /// Starts a backup of everything and waits for the discovery dialog.
    /// </summary>
    /// <returns>The dialog window.</returns>
    private AutomationElement OpenDialog()
    {
        _session.Invoke("BackupAllButton");
        return _session.WaitForWindow("DiscoveryReviewWindow", UiTimeouts.Run);
    }
}
