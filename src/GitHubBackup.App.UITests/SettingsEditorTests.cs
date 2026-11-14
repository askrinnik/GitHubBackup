using System.Text.Json;
using GitHubBackup.App.ViewModels;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Opens the settings editor from the main window and checks validation at the fields, saving and leaving with
/// unsaved changes.
/// </summary>
public sealed class SettingsEditorTests : IDisposable
{
    private const string _git = @"C:\Tools\Git\cmd\git.exe";

    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private readonly string _originalConfig;
    private readonly AppSession _session;

    /// <summary>
    /// Writes a configuration with one account and one standalone repository and starts the application.
    /// </summary>
    public SettingsEditorTests()
    {
        _installation = new(_api.Url);
        _installation.NewBackupConfig()
            .WithAccount("acme", ("tool", 2))
            .WithRepository("octo", "alpha", 1)
            .WriteTo(_installation.BackupConfigPath);
        _originalConfig = _installation.ReadBackupConfig();
        _session = AppSession.Start(_installation);
    }

    [Fact]
    public void Save_EditedFields_WritesBackupConfigAndReturnsToTree()
    {
        var newRoot = Path.Combine(_installation.FolderPath, "moved");
        OpenSettings();
        _session.SetText("BackupRootBox", newRoot);
        _session.SetText("GlobalExcludeBox", "tmp-*, octo/old");

        _session.Invoke("SaveSettingsButton");

        _session.WaitFor("RepositoryTree");
        _session.WaitUntilGone("SaveSettingsButton");
        ReadText(_installation.BackupConfigPath).ShouldBe(
            $$"""
            {
              "schemaVersion": 1,
              "backupRoot": {{Json(newRoot)}},
              "sourcesRoot": null,
              "exclude": [
                "tmp-*",
                "octo/old"
              ],
              "discoverAccessibleRepositories": false,
              "accounts": [
                {
                  "url": "https://github.com/acme",
                  "path": null,
                  "exclude": [],
                  "repositories": [
                    {
                      "name": "tool",
                      "id": 2,
                      "branch": null,
                      "path": null,
                      "status": "Active"
                    }
                  ]
                }
              ],
              "repositories": [
                {
                  "url": "https://github.com/octo/alpha",
                  "id": 1,
                  "branch": null,
                  "path": null,
                  "status": "Active"
                }
              ]
            }
            """.ReplaceLineEndings());
    }

    [Fact]
    public void EditField_EmptyBackupRoot_ShowsErrorAtField()
    {
        OpenSettings();
        var backupRoot = _session.WaitFor("BackupRootBox");

        AppSession.SetText(backupRoot, string.Empty);

        AppSession.WaitUntil(
            () => _session.FieldError(backupRoot) == SettingsFormViewModel.RequiredPathMessage,
            "the error below the archive root");
    }

    [Fact]
    public void Save_InvalidAccountUrl_ShowsErrorAndWritesNothing()
    {
        OpenSettings();
        var accountUrl = _session.WaitFor("AccountUrlBox");
        AppSession.SetText(accountUrl, "https://example.com/acme");

        _session.Invoke("SaveSettingsButton");

        AppSession.WaitUntil(() => _session.FieldError(accountUrl) is not null, "the error below the account URL");
        _session.FieldError(accountUrl).ShouldBe("accounts[0].url must be an account URL of the form https://github.com/<login>.");
        _session.WaitFor("SaveSettingsButton");
        _installation.ReadBackupConfig().ShouldBe(_originalConfig);
    }

    [Fact]
    public void Cancel_UnsavedChangesConfirmed_ReturnsToTreeWithoutWriting()
    {
        OpenSettings();
        _session.SetText("GlobalExcludeBox", "tmp-*");

        _session.Invoke("CancelSettingsButton");
        var messageBox = _session.WaitForMessageBox(SettingsViewModel.DiscardTitle);
        var text = AppSession.MessageText(messageBox);
        AppSession.Answer(messageBox, MessageBoxButtonId.Yes);

        text.ShouldBe(SettingsViewModel.DiscardMessage);
        _session.WaitFor("RepositoryTree");
        _session.WaitUntilGone("CancelSettingsButton");
        _installation.ReadBackupConfig().ShouldBe(_originalConfig);
    }

    [Fact]
    public void Cancel_UnsavedChangesDeclined_StaysInEditor()
    {
        OpenSettings();
        _session.SetText("GlobalExcludeBox", "tmp-*");

        _session.Invoke("CancelSettingsButton");
        AppSession.Answer(_session.WaitForMessageBox(SettingsViewModel.DiscardTitle), MessageBoxButtonId.No);

        AppSession.WaitUntil(
            () => _session.WaitFor("GlobalExcludeBox").Patterns.Value.Pattern.Value.Value == "tmp-*"
                && _session.MainWindow.FindFirstDescendant(condition => condition.ByName(SettingsViewModel.DiscardTitle)) is null,
            "the editor with the edited field and without the dialog");
        _installation.ReadBackupConfig().ShouldBe(_originalConfig);
    }

    [Fact]
    public void SaveTools_AbsoluteGitPath_WritesUserSettingsInContentRoot()
    {
        OpenSettings();
        _session.SetText("GitPathBox", _git);

        _session.Invoke("SaveToolsButton");

        AppSession.WaitUntil(
            () => _session.WaitFor("ToolsStatus").Name == SettingsViewModel.ToolsSavedMessage, "the saved tool paths");
        ReadText(_installation.UserSettingsPath).ShouldBe(
            $$"""
            {
              "Tools": {
                "GitPath": {{Json(_git)}},
                "SevenZipPath": "C:\\Program Files\\7-Zip\\7z.exe"
              }
            }
            """.ReplaceLineEndings());
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
    /// Opens the settings editor and waits until it has read the configuration.
    /// </summary>
    private void OpenSettings()
    {
        _session.WaitFor("RepositoryTree");
        _session.Invoke("SettingsButton");
        var save = _session.WaitFor("SaveSettingsButton");
        AppSession.WaitUntil(() => save.IsEnabled, "the settings to be read");
    }

    /// <summary>
    /// Reads a file with the line endings of the platform and without the final line break.
    /// </summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <returns>The text of the file.</returns>
    private static string ReadText(string path) => File.ReadAllText(path).ReplaceLineEndings().TrimEnd();

    /// <summary>
    /// Serializes <paramref name="value"/> as a JSON string.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The JSON string.</returns>
    private static string Json(string value) => JsonSerializer.Serialize(value);
}
