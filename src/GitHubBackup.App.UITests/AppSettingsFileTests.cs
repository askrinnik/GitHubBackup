using System.Text.Json;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Checks how the settings editor shares <c>appsettings.json</c> with the user: the application starts without the
/// file, shows values edited by hand and refuses to write over a file that is not a JSON object.
/// </summary>
public sealed class AppSettingsFileTests : IDisposable
{
    private const string _manualGit = @"C:\Manual\Git\cmd\git.exe";
    private const string _savedGit = @"D:\Saved\Git\cmd\git.exe";

    private readonly GitHubApiStub _api = new();
    private AppInstallation? _installation;
    private AppSession? _session;

    [Fact]
    public void Start_NoAppSettings_ShowsDefaultToolPathsAndSaveCreatesFileWithToolsOnly()
    {
        var installation = Install(writeAppSettings: false);
        var session = OpenSettings(installation);

        Text(session, "SevenZipPathBox").ShouldBe(ToolsOptions.DefaultSevenZipPath);
        session.SetText("GitPathBox", _savedGit);
        session.Invoke("SaveSettingsButton");

        session.WaitFor("RepositoryTree");
        session.WaitUntilGone("SaveSettingsButton");
        File.ReadAllText(installation.AppSettingsPath).ShouldContain("\"Tools\"");
        File.ReadAllText(installation.AppSettingsPath).ShouldNotContain("\"GitHub\"");
        File.Exists(installation.LegacyUserSettingsPath).ShouldBeFalse();
    }

    [Fact]
    public void Open_ManualEditInOtherCase_ShowsValueAndSaveReplacesKeyWithoutDuplicate()
    {
        var installation = Install(writeAppSettings: false);
        installation.WriteAppSettings($$"""
            { "tools": { "gitpath": {{JsonSerializer.Serialize(_manualGit)}} }, "Serilog": { "MinimumLevel": { "Default": "Debug" } } }
            """);
        var session = OpenSettings(installation);

        Text(session, "GitPathBox").ShouldBe(_manualGit);
        session.SetText("GitPathBox", _savedGit);
        session.Invoke("SaveSettingsButton");

        session.WaitFor("RepositoryTree");
        session.WaitUntilGone("SaveSettingsButton");
        var text = File.ReadAllText(installation.AppSettingsPath);
        text.ShouldNotContain("\"gitpath\"", Case.Sensitive);
        text.ShouldNotContain("\"tools\"", Case.Sensitive);
        text.ShouldContain("\"GitPath\"");
        text.ShouldContain("\"Default\": \"Debug\"");
        File.Exists(installation.AppSettingsPath + ".bak").ShouldBeTrue();
    }

    [Fact]
    public void Save_AppSettingsBrokenAfterStart_ShowsErrorKeepsEditorAndLeavesFile()
    {
        var installation = Install(writeAppSettings: true);
        var session = OpenSettings(installation);
        const string broken = "{ \"Tools\": ";
        installation.WriteAppSettings(broken);
        session.SetText("GitPathBox", _savedGit);

        session.Invoke("SaveSettingsButton");

        var messageBox = session.WaitForMessageBox(SettingsViewModel.SaveFailedTitle);
        var message = AppSession.MessageText(messageBox);
        AppSession.Close(messageBox);
        message.ShouldContain(installation.AppSettingsPath);
        session.WaitFor("SaveSettingsButton");
        File.ReadAllText(installation.AppSettingsPath).ShouldBe(broken);
    }

    /// <summary>
    /// Stops the application, then deletes the installation and stops the API stub.
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
        _installation?.Dispose();
        _api.Dispose();
    }

    /// <summary>
    /// Returns the text of a text box.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="automationId">The automation id of the text box.</param>
    /// <returns>The text.</returns>
    private static string Text(AppSession session, string automationId) =>
        session.WaitFor(automationId).Patterns.Value.Pattern.Value.Value;

    /// <summary>
    /// Creates the installation with one standalone repository.
    /// </summary>
    /// <param name="writeAppSettings">Whether the installation writes <c>appsettings.json</c>.</param>
    /// <returns>The installation, disposed with the test.</returns>
    private AppInstallation Install(bool writeAppSettings)
    {
        _installation = new(_api.Url, writeAppSettings: writeAppSettings);
        _installation.NewBackupConfig().WithRepository("octo", "alpha", 1).WriteTo(_installation.BackupConfigPath);
        return _installation;
    }

    /// <summary>
    /// Starts the application and opens the settings editor once it has read the settings.
    /// </summary>
    /// <param name="installation">The installation to start.</param>
    /// <returns>The session, disposed with the test.</returns>
    private AppSession OpenSettings(AppInstallation installation)
    {
        var session = _session = AppSession.Start(installation);
        session.WaitFor("RepositoryTree");
        session.Invoke("SettingsButton");
        var save = session.WaitFor("SaveSettingsButton");
        AppSession.WaitUntil(() => save.IsEnabled, "the settings to be read");
        return session;
    }
}
