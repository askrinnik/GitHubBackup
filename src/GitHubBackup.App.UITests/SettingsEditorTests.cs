using System.Text.Json;
using FlaUI.Core.AutomationElements;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Opens the settings editor from the main window and checks validation at the fields, saving and leaving with
/// unsaved changes.
/// </summary>
public sealed class SettingsEditorTests : IDisposable
{
    private const string _git = @"C:\Tools\Git\cmd\git.exe";

    /// <summary>The 7-Zip path that the settings of the installation configure.</summary>
    private const string _sevenZip = @"C:\Program Files\7-Zip\7z.exe";

    /// <summary>The value of a header that the tests type into the headers field.</summary>
    private const string _headersSecret = "ui-test-secret";

    /// <summary>The header list that the tests type into the headers field.</summary>
    private const string _headersValue = "api-key=" + _headersSecret;

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
    public void Open_ShowsFourGroupsWithHeadersAndTheirFields()
    {
        OpenSettings();

        var general = _session.WaitFor("SettingsGeneralGroup");
        var accounts = _session.WaitFor("SettingsAccountsGroup");
        var tools = _session.WaitFor("SettingsToolsGroup");
        var token = _session.WaitFor("SettingsTokenGroup");
        general.Name.ShouldBe("General");
        accounts.Name.ShouldBe("Accounts and repositories");
        tools.Name.ShouldBe("Tools");
        token.Name.ShouldBe("GitHub token");
        AppSession.WaitFor(general, "BackupRootBox");
        AppSession.WaitFor(general, "DiscoverAccessibleRepositoriesCheckBox");
        AppSession.WaitFor(general, "ThemeComboBox").IsEnabled.ShouldBeTrue();
        AppSession.WaitFor(accounts, "AccountsList");
        AppSession.WaitFor(accounts, "RepositoriesList");
        AppSession.WaitFor(tools, "GitPathBox");
        AppSession.WaitFor(token, "TokenBox");
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
    public void Save_AbsoluteGitPath_WritesAppSettingsKeepingOtherSectionsAndReturnsToTree()
    {
        var before = File.ReadAllText(_installation.AppSettingsPath);
        OpenSettings();
        _session.SetText("GitPathBox", _git);

        _session.Invoke("SaveSettingsButton");

        _session.WaitFor("RepositoryTree");
        _session.WaitUntilGone("SaveSettingsButton");
        using var document = JsonDocument.Parse(File.ReadAllText(_installation.AppSettingsPath));
        var root = document.RootElement;
        root.GetProperty("Tools").GetProperty("GitPath").GetString().ShouldBe(_git);
        root.GetProperty("Tools").GetProperty("SevenZipPath").GetString().ShouldBe(_sevenZip);
        root.GetProperty("GitHub").GetProperty("Token").GetString().ShouldBe(AppInstallation.Token);
        root.GetProperty("Backup").GetProperty("ShortHashLength").GetInt32().ShouldBe(10);
        root.GetProperty("Serilog").GetProperty("MinimumLevel").GetProperty("Default").GetString().ShouldBe("Information");
        root.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["GitHub", "Tools", "Backup", "History", "OpenTelemetry", "Serilog"]);
        File.ReadAllText(_installation.AppSettingsPath + ".bak").ShouldBe(before);
        File.Exists(_installation.UiSettingsPath).ShouldBeFalse();
        File.Exists(_installation.LegacyUserSettingsPath).ShouldBeFalse();
    }

    [Fact]
    public void Open_ShowsTelemetryGroupWithItsFields()
    {
        OpenSettings();

        var telemetry = _session.WaitFor("SettingsTelemetryGroup");

        telemetry.Name.ShouldBe("Telemetry");
        AppSession.WaitFor(telemetry, "TelemetryEnabledCheckBox");
        AppSession.WaitFor(telemetry, "OtlpEndpointBox");
        AppSession.WaitFor(telemetry, "TelemetryHeadersBox").Properties.IsPassword.Value.ShouldBeTrue();
        AppSession.WaitFor(telemetry, "ShowTelemetryHeadersToggle").Name.ShouldBe("Show OTLP headers");
        telemetry.FindFirstDescendant(condition => condition.ByAutomationId("TelemetryHeadersTextBox")).ShouldBeNull();
        telemetry.FindFirstDescendant(condition => condition.ByAutomationId("TelemetryHeadersOverriddenNote")).ShouldBeNull();
    }

    [Fact]
    public void Save_TelemetryHeadersTyped_WritesHeadersToAppSettings()
    {
        OpenSettings();
        _session.SetText("TelemetryHeadersBox", " api-key=ui-test-secret ");

        _session.Invoke("SaveSettingsButton");

        _session.WaitFor("RepositoryTree");
        _session.WaitUntilGone("SaveSettingsButton");
        using var document = JsonDocument.Parse(File.ReadAllText(_installation.AppSettingsPath));
        document.RootElement.GetProperty("OpenTelemetry").GetProperty("Headers").GetString().ShouldBe("api-key=ui-test-secret");
        File.Exists(_installation.AppSettingsPath + ".bak").ShouldBeTrue();
    }

    [Fact]
    public void ShowToggle_RevealsAndHidesValue()
    {
        OpenSettings();
        _session.SetText("TelemetryHeadersBox", _headersValue);

        _session.WaitFor("ShowTelemetryHeadersToggle").Patterns.Toggle.Pattern.Toggle();

        var shown = _session.WaitFor("TelemetryHeadersTextBox");
        shown.Patterns.Value.Pattern.Value.Value.ShouldBe(_headersValue);
        _session.WaitUntilGone("TelemetryHeadersBox");

        _session.WaitFor("ShowTelemetryHeadersToggle").Patterns.Toggle.Pattern.Toggle();

        _session.WaitFor("TelemetryHeadersBox").Properties.IsPassword.Value.ShouldBeTrue();
        _session.WaitUntilGone("TelemetryHeadersTextBox");
    }

    [Fact]
    public void TelemetryHeadersFields_AutomationNamesNeverContainValue()
    {
        OpenSettings();
        _session.SetText("TelemetryHeadersBox", _headersValue);
        var telemetry = _session.WaitFor("SettingsTelemetryGroup");

        AssertNoElementExposes(telemetry, _headersSecret);
        _session.WaitFor("ShowTelemetryHeadersToggle").Patterns.Toggle.Pattern.Toggle();
        _session.WaitFor("TelemetryHeadersTextBox");

        AssertNoElementExposes(telemetry, _headersSecret);
    }

    [Fact]
    public void Open_HeadersVariableSet_ShowsOverriddenNoteAndFileValueOnly()
    {
        using var api = new GitHubApiStub();
        using var installation = new AppInstallation(
            api.Url,
            environmentVariables: new Dictionary<string, string>
            {
                [GitHubBackupHostBuilder.EnvironmentVariablePrefix + "OpenTelemetry__Headers"] = "api-key=variable-secret",
            });
        installation.WriteAppSettings($$"""
            {
              "GitHub": { "ApiBaseUrl": {{Json(api.Url)}}, "Token": "{{AppInstallation.Token}}" },
              "Tools": { "SevenZipPath": {{Json(_sevenZip)}} },
              "OpenTelemetry": { "Enabled": false, "OtlpEndpoint": "http://localhost:4317", "Headers": "api-key=file-secret" }
            }
            """);
        installation.NewBackupConfig().WriteTo(installation.BackupConfigPath);
        using var session = AppSession.Start(installation);
        session.WaitFor("RepositoryTree");
        session.Invoke("SettingsButton");
        var save = session.WaitFor("SaveSettingsButton");
        AppSession.WaitUntil(() => save.IsEnabled, "the settings to be read");

        session.WaitFor("TelemetryHeadersOverriddenNote").Name.ShouldBe(SettingsViewModel.HeadersOverriddenMessage);
        session.WaitFor("ShowTelemetryHeadersToggle").Patterns.Toggle.Pattern.Toggle();
        session.WaitFor("TelemetryHeadersTextBox").Patterns.Value.Pattern.Value.Value.ShouldBe("api-key=file-secret");
        AssertNoElementExposes(session.MainWindow, "variable-secret");
    }

    [Fact]
    public void Open_ShowsOneSaveButtonWithoutGroupSaveButtons()
    {
        OpenSettings();

        _session.WaitFor("SaveSettingsButton").Name.ShouldBe("Save");
        foreach (var removed in new[] { "SaveToolsButton", "SaveTelemetryButton", "SaveTokenButton" })
        {
            _session.MainWindow.FindFirstDescendant(condition => condition.ByAutomationId(removed)).ShouldBeNull();
        }
    }

    [Fact]
    public void Save_TelemetryEnabledWithEndpoint_WritesOpenTelemetrySection()
    {
        OpenSettings();
        _session.WaitFor("TelemetryEnabledCheckBox").Patterns.Toggle.Pattern.Toggle();
        _session.SetText("OtlpEndpointBox", "http://localhost:4318");

        _session.Invoke("SaveSettingsButton");

        _session.WaitFor("RepositoryTree");
        _session.WaitUntilGone("SaveSettingsButton");
        using var document = JsonDocument.Parse(File.ReadAllText(_installation.AppSettingsPath));
        var section = document.RootElement.GetProperty("OpenTelemetry");
        section.GetProperty("Enabled").GetBoolean().ShouldBeTrue();
        section.GetProperty("OtlpEndpoint").GetString().ShouldBe("http://localhost:4318");
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
    /// Asserts that no element below <paramref name="root"/> has <paramref name="secret"/> in its name or help text.
    /// </summary>
    /// <param name="root">The element whose descendants are checked.</param>
    /// <param name="secret">The text that must not appear.</param>
    private static void AssertNoElementExposes(AutomationElement root, string secret)
    {
        foreach (var element in root.FindAllDescendants())
        {
            (element.Properties.Name.ValueOrDefault ?? string.Empty).ShouldNotContain(secret);
            (element.Properties.HelpText.ValueOrDefault ?? string.Empty).ShouldNotContain(secret);
        }
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
