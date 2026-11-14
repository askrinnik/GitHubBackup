using System.Text.Json;
using GitHubBackup.App.ViewModels;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Starts the application without <c>backup-config.json</c> and checks that the settings editor creates the file only
/// on a valid Save and that the main window then shows the configuration without a restart.
/// </summary>
public sealed class FirstRunTests : IDisposable
{
    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private readonly AppSession _session;

    /// <summary>
    /// Installs the application without a backup configuration and starts it.
    /// </summary>
    public FirstRunTests()
    {
        _installation = new(_api.Url);
        _session = AppSession.Start(_installation);
    }

    [Fact]
    public void Save_FilledBackupRoot_CreatesConfigWithoutBackupAndShowsTree()
    {
        OpenSettingsFromFirstRun();
        _session.WaitFor("SettingsNewConfigMessage").Name
            .ShouldBe(SettingsViewModel.FormatNewConfigMessage(_installation.BackupConfigPath));
        _session.SetText("BackupRootBox", _installation.ArchivesPath);

        _session.Invoke("SaveSettingsButton");

        _session.WaitFor("EmptyTreeMessage");
        _session.WaitUntilGone("SaveSettingsButton");
        _session.MainWindow.FindFirstDescendant(condition => condition.ByAutomationId("ConfigMissingMessage")).ShouldBeNull();
        File.ReadAllText(_installation.BackupConfigPath).ReplaceLineEndings().TrimEnd().ShouldBe(
            $$"""
            {
              "schemaVersion": 1,
              "backupRoot": {{JsonSerializer.Serialize(_installation.ArchivesPath)}},
              "sourcesRoot": null,
              "exclude": [],
              "discoverAccessibleRepositories": false,
              "accounts": [],
              "repositories": []
            }
            """.ReplaceLineEndings());
        File.Exists(_installation.BackupConfigPath + ".bak").ShouldBeFalse();
    }

    [Fact]
    public void Save_EmptyBackupRoot_ShowsErrorAtFieldAndCreatesNothing()
    {
        OpenSettingsFromFirstRun();
        var backupRoot = _session.WaitFor("BackupRootBox");

        _session.Invoke("SaveSettingsButton");

        AppSession.WaitUntil(() => _session.FieldError(backupRoot) is not null, "the error below the archive root");
        _session.WaitFor("SaveSettingsButton");
        File.Exists(_installation.BackupConfigPath).ShouldBeFalse();
    }

    [Fact]
    public void Cancel_WithoutSave_ReturnsToFirstRunMessageAndCreatesNothing()
    {
        OpenSettingsFromFirstRun();

        _session.Invoke("CancelSettingsButton");

        _session.WaitFor("ConfigMissingMessage").Name.ShouldBe(RepositoriesViewModel.ConfigMissingMessage);
        _session.WaitUntilGone("CancelSettingsButton");
        File.Exists(_installation.BackupConfigPath).ShouldBeFalse();
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
    /// Opens the settings editor from the first-run message and waits until it holds the empty configuration.
    /// </summary>
    private void OpenSettingsFromFirstRun()
    {
        _session.Invoke("OpenSettingsFromFirstRunButton");
        var save = _session.WaitFor("SaveSettingsButton");
        AppSession.WaitUntil(() => save.IsEnabled, "the empty configuration to be loaded");
    }
}
