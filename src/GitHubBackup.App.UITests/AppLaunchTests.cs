using FlaUI.Core.Definitions;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Starts the application with valid and invalid configurations and checks its first screen and its exit code.
/// </summary>
public sealed class AppLaunchTests : IDisposable
{
    /// <summary>The title of the dialogs the application shows.</summary>
    private const string _applicationTitle = "GitHub Backup";

    /// <summary>The first line of the dialog that lists configuration errors.</summary>
    private const string _configurationErrorHeading = "Configuration error:";

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private AppSession? _session;

    /// <summary>
    /// Installs the application for the API stub.
    /// </summary>
    public AppLaunchTests() => _installation = new(_api.Url);

    [Fact]
    public void Launch_ValidConfiguration_ShowsMainWindowWithRepositoryTree()
    {
        _installation.NewBackupConfig().WithRepository("octo", "alpha", 1).WriteTo(_installation.BackupConfigPath);

        var session = Start();

        var tree = session.WaitFor("RepositoryTree");
        AppSession.WaitUntil(
            () => tree.FindFirstDescendant(condition =>
                condition.ByControlType(ControlType.TreeItem).And(condition.ByName("octo/alpha"))) is not null,
            "the tree item of octo/alpha");
    }

    [Fact]
    public void Launch_ConfigurationWithoutRepositories_ShowsEmptyTreeMessage()
    {
        _installation.NewBackupConfig().WriteTo(_installation.BackupConfigPath);

        var session = Start();

        session.WaitFor("EmptyTreeMessage").Name.ShouldBe("backup-config.json lists no accounts and no repositories.");
    }

    [Fact]
    public void Launch_BackupConfigMissing_ShowsFirstRunMessageAndDisablesBackup()
    {
        var session = Start();

        session.WaitFor("ConfigMissingMessage").Name.ShouldBe(RepositoriesViewModel.ConfigMissingMessage);
        session.WaitFor("OpenSettingsFromFirstRunButton").IsEnabled.ShouldBeTrue();
        session.WaitFor("BackupAllButton").IsEnabled.ShouldBeFalse();
        session.WaitFor("BackupAccountButton").IsEnabled.ShouldBeFalse();
        session.WaitFor("BackupRepositoryButton").IsEnabled.ShouldBeFalse();
        session.MainWindow.FindFirstDescendant(condition => condition.ByAutomationId("TreeLoadError")).ShouldBeNull();
        File.Exists(_installation.BackupConfigPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Launch_InvalidAppSettings_ShowsConfigurationErrorAndExitsCritical()
    {
        _installation.WriteAppSettings("{ \"GitHub\": ");
        var session = Start();

        // The dialog replaces the main window, so it takes as long as a start of the application.
        var messageBox = session.WaitForMessageBox(_applicationTitle, UiTimeouts.Launch);
        var text = AppSession.MessageText(messageBox);
        AppSession.Close(messageBox);

        text.ShouldStartWith(_configurationErrorHeading);
        (await session.WaitForExitAsync()).ShouldBe((int)ExitCode.Critical);
    }

    [Fact]
    public async Task Close_MainWindow_ExitsWithSuccess()
    {
        _installation.NewBackupConfig().WriteTo(_installation.BackupConfigPath);
        var session = Start();
        session.WaitFor("EmptyTreeMessage");

        session.CloseMainWindow();

        (await session.WaitForExitAsync()).ShouldBe((int)ExitCode.Success);
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
    /// Starts the application of the installation.
    /// </summary>
    /// <returns>The session, disposed with the test.</returns>
    private AppSession Start() => _session = AppSession.Start(_installation);
}
