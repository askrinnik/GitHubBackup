using System.Text.Json;
using FlaUI.Core.AutomationElements;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Opens the settings editor from the main window and checks the navigation between its sections, validation at the
/// fields, saving and leaving with unsaved changes.
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
    public void Open_ShowsNavigationWithGeneralSelected()
    {
        OpenSettings();

        var list = _session.WaitFor("SettingsSectionList");
        var items = list.FindAllChildren(condition => condition.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        items.Select(item => item.AutomationId).ShouldBe(
        [
            "SettingsSectionGeneral", "SettingsSectionAccounts", "SettingsSectionRepositories",
            "SettingsSectionTools", "SettingsSectionTelemetry", "SettingsSectionToken",
        ]);
        items.Select(item => item.Name).ShouldBe(["General", "Accounts", "Repositories", "Tools", "Telemetry", "GitHub token"]);
        items[0].Patterns.SelectionItem.Pattern.IsSelected.Value.ShouldBeTrue();
        var general = _session.WaitFor("SettingsGeneralGroup");
        general.Name.ShouldBe("General");
        AppSession.WaitFor(general, "BackupRootBox");
        AppSession.WaitFor(general, "DiscoverAccessibleRepositoriesCheckBox");
        AppSession.WaitFor(general, "ThemeComboBox").IsEnabled.ShouldBeTrue();
        foreach (var hidden in new[] { "SettingsAccountsGroup", "SettingsRepositoriesGroup", "SettingsToolsGroup", "SettingsTelemetryGroup", "SettingsTokenGroup" })
        {
            _session.MainWindow.FindFirstDescendant(condition => condition.ByAutomationId(hidden)).ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(SettingsSection.Accounts, "SettingsAccountsGroup", "Accounts", "AccountsList", "AccountUrlBox", "AccountRepositoriesList")]
    [InlineData(SettingsSection.Repositories, "SettingsRepositoriesGroup", "Repositories", "RepositoriesList", "RepositoryFilterBox", "AddRepositoryButton")]
    [InlineData(SettingsSection.Tools, "SettingsToolsGroup", "Tools", "GitPathBox", "SevenZipPathBox", "GitPathBox")]
    [InlineData(SettingsSection.Telemetry, "SettingsTelemetryGroup", "Telemetry", "TelemetryEnabledCheckBox", "OtlpEndpointBox", "TelemetryHeadersBox")]
    [InlineData(SettingsSection.Token, "SettingsTokenGroup", "GitHub token", "TokenBox", "CheckTokenButton", "TokenBox")]
    public void SelectSection_ShowsOnlyThatSectionWithItsFields(
        SettingsSection section, string groupId, string header, string field1, string field2, string field3)
    {
        OpenSettings();

        SelectSection(section);

        var group = _session.WaitFor(groupId);
        group.Name.ShouldBe(header);
        AppSession.WaitFor(group, field1);
        AppSession.WaitFor(group, field2);
        AppSession.WaitFor(group, field3);
        _session.WaitUntilGone("SettingsGeneralGroup");
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
        SelectSection(SettingsSection.Accounts);
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
        SelectSection(SettingsSection.Tools);
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
        SelectSection(SettingsSection.Telemetry);

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
    public void Save_TelemetryHeadersTyped_WritesHeadersToCredentialManager()
    {
        OpenSettings();
        SelectSection(SettingsSection.Telemetry);
        _session.SetText("TelemetryHeadersBox", " api-key=ui-test-secret ");

        _session.Invoke("SaveSettingsButton");

        _session.WaitFor("RepositoryTree");
        _session.WaitUntilGone("SaveSettingsButton");
        _installation.OtlpHeadersCredential.Read().ShouldBe("api-key=ui-test-secret");
        using var document = JsonDocument.Parse(File.ReadAllText(_installation.AppSettingsPath));
        document.RootElement.GetProperty("OpenTelemetry").TryGetProperty("Headers", out _).ShouldBeFalse();
        File.ReadAllText(_installation.AppSettingsPath).ShouldNotContain(_headersSecret);
    }

    [Fact]
    public void Open_HeadersInAppSettings_ShowsMigrationNote()
    {
        using var api = new GitHubApiStub();
        using var installation = new AppInstallation(api.Url);
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
        session.Select("SettingsSectionTelemetry");

        session.WaitFor("TelemetryHeadersInAppSettingsNote").Name.ShouldBe(SettingsViewModel.HeadersInAppSettingsMessage);
        AssertNoElementExposes(session.MainWindow, "file-secret");

        AppSession.Invoke(save);
        var messageBox = session.WaitForMessageBox(SettingsViewModel.HeadersMovedTitle);
        AppSession.MessageText(messageBox).ShouldBe(SettingsViewModel.HeadersMovedMessage);
        AppSession.Close(messageBox);

        session.WaitFor("RepositoryTree");
        installation.OtlpHeadersCredential.Read().ShouldBe("api-key=file-secret");
        File.ReadAllText(installation.AppSettingsPath).ShouldNotContain("file-secret");
        File.ReadAllText(installation.AppSettingsPath + ".bak").ShouldContain("file-secret");
    }

    [Fact]
    public void ShowToggle_RevealsAndHidesValue()
    {
        OpenSettings();
        SelectSection(SettingsSection.Telemetry);
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
        SelectSection(SettingsSection.Telemetry);
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
        session.Select("SettingsSectionTelemetry");

        session.WaitFor("TelemetryHeadersOverriddenNote").Name.ShouldBe(SettingsViewModel.HeadersOverriddenMessage);
        session.WaitFor("ShowTelemetryHeadersToggle").Patterns.Toggle.Pattern.Toggle();
        session.WaitFor("TelemetryHeadersTextBox").Patterns.Value.Pattern.Value.Value.ShouldBe("api-key=file-secret");
        AssertNoElementExposes(session.MainWindow, "variable-secret");
    }

    [Theory]
    [InlineData(SettingsSection.General, "SettingsGeneralGroup", "SettingsFoldersCard,SettingsDiscoveryCard,SettingsAppearanceCard")]
    [InlineData(SettingsSection.Accounts, "SettingsAccountsGroup", "SettingsAccountListCard,SettingsAccountCard,SettingsAccountRepositoriesCard")]
    [InlineData(SettingsSection.Repositories, "SettingsRepositoriesGroup", "SettingsRepositoriesCard")]
    [InlineData(SettingsSection.Tools, "SettingsToolsGroup", "SettingsToolsCard")]
    [InlineData(SettingsSection.Telemetry, "SettingsTelemetryGroup", "SettingsTelemetryCard")]
    [InlineData(SettingsSection.Token, "SettingsTokenGroup", "SettingsTokenCard,SettingsTokenCheckResultCard")]
    public void SelectSection_ShowsCardsOfTheSectionBelowEachOther(SettingsSection section, string groupId, string cardIds)
    {
        OpenSettings();
        _session.WaitFor("SettingsNavigationCard");

        SelectSection(section);

        var group = _session.WaitFor(groupId);
        var cards = cardIds.Split(',').Select(id => AppSession.WaitFor(group, id)).ToArray();
        foreach (var card in cards)
        {
            card.ControlType.ShouldBe(FlaUI.Core.Definitions.ControlType.Group);
            card.Name.ShouldNotBeNullOrEmpty();
        }

        // The cards stand below or to the right of each other and never overlap.
        for (var i = 1; i < cards.Length; i++)
        {
            var previous = cards[i - 1].BoundingRectangle;
            var next = cards[i].BoundingRectangle;
            (next.Top >= previous.Bottom || next.Left >= previous.Right).ShouldBeTrue($"{cards[i].AutomationId} overlaps {cards[i - 1].AutomationId}");
        }
    }

    [Fact]
    public void Open_ShowsSaveRightOfCancelAtTheRightEdge()
    {
        OpenSettings();

        var cancel = _session.WaitFor("CancelSettingsButton").BoundingRectangle;
        var save = _session.WaitFor("SaveSettingsButton").BoundingRectangle;
        var window = _session.MainWindow.BoundingRectangle;
        save.Left.ShouldBeGreaterThan(cancel.Right);
        ((double)save.Top).ShouldBe(cancel.Top, 2);
        (window.Right - save.Right).ShouldBeLessThan(save.Width);
        cancel.Left.ShouldBeGreaterThan(window.Left + (window.Width / 2));
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
        SelectSection(SettingsSection.Telemetry);
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

    [Fact]
    public void Save_AccountErrorFromGeneral_OpensAccountsAndMarksSectionAndAccount()
    {
        OpenSettings();
        SelectSection(SettingsSection.Accounts);
        _session.SetText("AccountUrlBox", "https://example.com/acme");
        SelectSection(SettingsSection.General);

        _session.Invoke("SaveSettingsButton");

        var accountsEntry = _session.WaitFor("SettingsSectionAccounts");
        AppSession.WaitUntil(() => accountsEntry.Patterns.SelectionItem.Pattern.IsSelected.Value, "the accounts section to open");
        AppSession.WaitFor(accountsEntry, "SettingsSectionErrorIcon").Name.ShouldBe("Has errors");
        AppSession.WaitFor(_session.WaitFor("AccountsList"), "AccountErrorIcon");
        _session.WaitFor("SettingsSectionGeneral").FindFirstDescendant(condition => condition.ByAutomationId("SettingsSectionErrorIcon"))
            .ShouldBeNull();
        _installation.ReadBackupConfig().ShouldBe(_originalConfig);
    }

    [Fact]
    public void AddAndRemoveAccount_UpdatesListAndDetails()
    {
        OpenSettings();
        SelectSection(SettingsSection.Accounts);
        var list = _session.WaitFor("AccountsList");

        _session.Invoke("AddAccountButton");

        AppSession.WaitUntil(() => AccountItems(list).Length == 2, "the new account in the list");
        var added = AccountItems(list)[1];
        added.Name.ShouldBe(AccountSettingsViewModel.NewAccountName);
        AppSession.WaitUntil(() => added.Patterns.SelectionItem.Pattern.IsSelected.Value, "the new account to be selected");
        _session.WaitFor("AccountUrlBox").Patterns.Value.Pattern.Value.Value.ShouldBeEmpty();

        _session.Invoke("RemoveAccountButton");
        AppSession.WaitUntil(() => AccountItems(list).Length == 1, "the account to be removed");
        _session.WaitFor("AccountUrlBox").Patterns.Value.Pattern.Value.Value.ShouldBe("https://github.com/acme");

        _session.Invoke("RemoveAccountButton");
        AppSession.WaitUntil(() => AccountItems(list).Length == 0, "the last account to be removed");
        _session.WaitFor("NoAccountsMessage").Name.ShouldBe("No accounts. Add an account.");
        _session.WaitUntilGone("RemoveAccountButton");
    }

    [Fact]
    public void RepositoryFilter_NoMatchThenCleared_ShowsMessageThenRows()
    {
        OpenSettings();
        SelectSection(SettingsSection.Repositories);

        _session.SetText("RepositoryFilterBox", "zzz");

        _session.WaitFor("RepositoriesNoMatch").Name.ShouldBe("No repositories match the filter.");
        _session.WaitUntilGone("RepositoryUrlBox");

        _session.SetText("RepositoryFilterBox", "ALPHA");

        _session.WaitFor("RepositoryUrlBox").Patterns.Value.Pattern.Value.Value.ShouldBe("https://github.com/octo/alpha");
        _session.WaitUntilGone("RepositoriesNoMatch");
    }

    [Fact]
    public void AccountRepositoryFilter_NoMatch_ShowsMessage()
    {
        OpenSettings();
        SelectSection(SettingsSection.Accounts);

        _session.SetText("AccountRepositoryFilterBox", "zzz");

        _session.WaitFor("AccountRepositoriesNoMatch");
        _session.WaitUntilGone("AccountRepositoryBranchBox");
    }

    [Fact]
    public void AddRepository_FilterActive_ClearsFilterAndShowsNewRow()
    {
        OpenSettings();
        SelectSection(SettingsSection.Repositories);
        _session.SetText("RepositoryFilterBox", "zzz");
        _session.WaitFor("RepositoriesNoMatch");

        _session.Invoke("AddRepositoryButton");

        AppSession.WaitUntil(
            () => _session.WaitFor("RepositoryFilterBox").Patterns.Value.Pattern.Value.Value.Length == 0,
            "the filter to be cleared");
        var list = _session.WaitFor("RepositoriesList");
        AppSession.WaitUntil(
            () => list.FindAllDescendants(condition => condition.ByAutomationId("RepositoryUrlBox")).Length == 2,
            "the existing and the new row");
        _session.WaitUntilGone("RepositoriesNoMatch");
    }

    [Fact]
    public void SwitchSection_TypedHeaders_KeepsValue()
    {
        OpenSettings();
        SelectSection(SettingsSection.Telemetry);
        _session.SetText("TelemetryHeadersBox", _headersValue);

        SelectSection(SettingsSection.Tools);
        SelectSection(SettingsSection.Telemetry);
        _session.WaitFor("ShowTelemetryHeadersToggle").Patterns.Toggle.Pattern.Toggle();

        _session.WaitFor("TelemetryHeadersTextBox").Patterns.Value.Pattern.Value.Value.ShouldBe(_headersValue);
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
    /// Opens a section of the editor through its entry in the navigation.
    /// </summary>
    /// <param name="section">The section.</param>
    private void SelectSection(SettingsSection section) => _session.Select($"SettingsSection{section}");

    /// <summary>
    /// Returns the entries of the list of accounts.
    /// </summary>
    /// <param name="list">The list of accounts.</param>
    /// <returns>The entries, in order.</returns>
    private static AutomationElement[] AccountItems(AutomationElement list) =>
        list.FindAllChildren(condition => condition.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));

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
