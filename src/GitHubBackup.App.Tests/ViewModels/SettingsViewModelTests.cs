using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Navigation;
using GitHubBackup.App.Shell;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Core.Tools;
using GitHubBackup.Core.Ui;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="SettingsViewModel"/>: reading and saving the configuration, the errors and warnings of the
/// validator, leaving the editor, the tool paths, saving the token and checking it.
/// </summary>
public sealed class SettingsViewModelTests
{
    private const string _token = "ghp_SettingsTokenValue0123456789abcdefAB";
    private const string _typedToken = "ghp_TypedTokenValue0123456789abcdefABCD";
    private const string _defaultSourcesRoot = @"C:\app\sources";
    private const string _storeLocation = "Windows Credential Manager (GitHubBackup:https://github.com)";
    private const string _configurationSource = "configuration (GitHub:Token)";
    private const string _configPath = @"C:\app\backup-config.json";
    private const int _backupConfigMissingEventId = 13;
    private const string _fileHeaders = "api-key=file-secret";

    private static readonly DateTimeOffset _resetAt = new(2026, 11, 14, 11, 30, 0, TimeSpan.Zero);

    private readonly IBackupConfigStore _configStore = Substitute.For<IBackupConfigStore>();
    private readonly IToolPathsStore _toolPathsStore = Substitute.For<IToolPathsStore>();
    private readonly ITelemetrySettingsStore _telemetryStore = Substitute.For<ITelemetrySettingsStore>();
    private readonly ITokenStore _tokenStore = Substitute.For<ITokenStore>();
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();
    private readonly IGitHubTokenInspector _inspector = Substitute.For<IGitHubTokenInspector>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly SecretMasker _masker = new();
    private readonly FakeLogger<SettingsViewModel> _logger = new();
    private readonly BackupConfigValidator _validator = new();
    private readonly IUiSettingsStore _uiSettingsStore = Substitute.For<IUiSettingsStore>();
    private readonly IThemeService _themeService = Substitute.For<IThemeService>();
    private readonly UiPreferences _preferences;
    private readonly SettingsViewModel _viewModel;

    /// <summary>
    /// Creates the editor over a store that returns <see cref="CreateConfig"/> and validates with the real validator.
    /// </summary>
    public SettingsViewModelTests()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => new BackupConfigLoadResult(CreateConfig(), []));
        _configStore.Validate(Arg.Any<BackupConfig>())
            .Returns(call => _validator.Validate(call.Arg<BackupConfig>(), _defaultSourcesRoot));
        _toolPathsStore.Current.Returns(new ToolPaths(null, @"C:\Program Files\7-Zip\7z.exe"));
        _toolPathsStore.Validate(Arg.Any<ToolPaths>()).Returns([]);
        _telemetryStore.Current.Returns(new TelemetrySettings(true, "http://localhost:4317", _fileHeaders));
        _telemetryStore.Validate(Arg.Any<TelemetrySettings>()).Returns([]);
        _tokenStore.Location.Returns(_storeLocation);
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new GitHubToken(_token, _storeLocation));
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        timeProvider.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+02", TimeSpan.FromHours(2), "UTC+02", "UTC+02"));
        _preferences = new(_uiSettingsStore, _themeService, NullLogger<UiPreferences>.Instance);
        _viewModel = new(
            _configStore, _toolPathsStore, _telemetryStore, _tokenStore, _tokenProvider, _inspector, _navigation, _dialogs, _masker, timeProvider, _logger, _preferences);
    }

    [Fact]
    public async Task Load_Config_FillsFieldsWithoutChanges()
    {
        await LoadAsync();

        _viewModel.IsLoaded.ShouldBeTrue();
        _viewModel.IsDirty.ShouldBeFalse();
        _viewModel.HasUnsavedChanges.ShouldBeFalse();
        _viewModel.BackupRoot.ShouldBe(@"D:\Backups");
        _viewModel.SourcesRoot.ShouldBe(@"E:\Sources");
        _viewModel.Exclude.ShouldBe("someone/*, *-archive");
        _viewModel.DiscoverAccessibleRepositories.ShouldBeFalse();
        var account = _viewModel.Accounts.ShouldHaveSingleItem();
        account.Url.ShouldBe("https://github.com/octo");
        account.Exclude.ShouldBe("test-*");
        account.Repositories.Select(repository => repository.Name).ShouldBe(["tool", "old"]);
        account.Repositories[1].Branch.ShouldBe("legacy");
        account.Repositories[1].IsUnavailable.ShouldBeTrue();
        _viewModel.Repositories.ShouldHaveSingleItem().Url.ShouldBe("https://github.com/other/lib");
        _viewModel.GitPath.ShouldBeEmpty();
        _viewModel.SevenZipPath.ShouldBe(@"C:\Program Files\7-Zip\7z.exe");
        _viewModel.Warnings.ShouldBeEmpty();
        _viewModel.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Load_Twice_ReadsOnce()
    {
        await LoadAsync();
        _viewModel.BackupRoot = @"D:\Other";

        await LoadAsync();

        _viewModel.BackupRoot.ShouldBe(@"D:\Other");
        await _configStore.Received(1).LoadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ThemeOptions_ListsSystemLightDarkWithSystemSelectedByDefault()
    {
        SettingsViewModel.ThemeOptions.Select(option => option.Value).ShouldBe([UiTheme.System, UiTheme.Light, UiTheme.Dark]);
        SettingsViewModel.ThemeOptions.Select(option => option.Label).ShouldBe(["System", "Light", "Dark"]);
        _viewModel.SelectedTheme.Value.ShouldBe(UiTheme.System);
    }

    [Fact]
    public void Constructor_SavedTheme_SelectsIt()
    {
        _preferences.Theme = UiTheme.Dark;

        var viewModel = new SettingsViewModel(
            _configStore, _toolPathsStore, _telemetryStore, _tokenStore, _tokenProvider, _inspector, _navigation, _dialogs, _masker,
            TimeProvider.System, _logger, _preferences);

        viewModel.SelectedTheme.Value.ShouldBe(UiTheme.Dark);
        _themeService.DidNotReceiveWithAnyArgs().Apply(default);
    }

    [Fact]
    public async Task SelectedTheme_Changed_AppliesAndSavesAtOnceWithoutUnsavedChanges()
    {
        await LoadAsync();

        _viewModel.SelectedTheme = SettingsViewModel.ThemeOptions[2];
        await _viewModel.ThemeChange;

        _themeService.Received(1).Apply(UiTheme.Dark);
        await _uiSettingsStore.Received(1).SaveAsync(
            Arg.Is<UiSettings>(settings => settings.Theme == UiTheme.Dark), Arg.Any<CancellationToken>());
        _preferences.Theme.ShouldBe(UiTheme.Dark);
        _viewModel.IsDirty.ShouldBeFalse();
        _viewModel.HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task SelectedTheme_ConfigNotReadable_StillAppliesAndSaves()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(
            new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set."]));
        await LoadAsync();

        _viewModel.SelectedTheme = SettingsViewModel.ThemeOptions[1];
        await _viewModel.ThemeChange;

        _viewModel.IsLoaded.ShouldBeFalse();
        _themeService.Received(1).Apply(UiTheme.Light);
        await _uiSettingsStore.Received(1).SaveAsync(Arg.Any<UiSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_AfterThemeChange_LeavesWithoutQuestionAndKeepsTheme()
    {
        await LoadAsync();
        _viewModel.SelectedTheme = SettingsViewModel.ThemeOptions[2];
        await _viewModel.ThemeChange;

        var canClose = await _viewModel.CanCloseAsync();

        canClose.ShouldBeTrue();
        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
        _preferences.Theme.ShouldBe(UiTheme.Dark);
    }

    [Fact]
    public async Task Load_ConfigError_ShowsMaskedErrorsAndKeepsSaveAvailable()
    {
        _masker.Register(_token);
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(
            new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set.", $"bad {_token}"]));

        await LoadAsync();

        _viewModel.IsLoaded.ShouldBeFalse();
        _viewModel.HasLoadError.ShouldBeTrue();
        _viewModel.LoadError.ShouldBe($"backupRoot must be set.{Environment.NewLine}bad ***");
        _viewModel.SaveCommand.CanExecute(null).ShouldBeTrue();
        _viewModel.IsNewConfig.ShouldBeFalse();
    }

    [Fact]
    public async Task Load_ConfigNotFound_FillsEmptyConfigAndShowsNewConfigMessage()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new BackupConfigNotFoundException(_configPath));

        await LoadAsync();

        _viewModel.IsLoaded.ShouldBeTrue();
        _viewModel.HasLoadError.ShouldBeFalse();
        _viewModel.IsNewConfig.ShouldBeTrue();
        _viewModel.NewConfigMessage.ShouldBe(@"The file 'C:\app\backup-config.json' does not exist yet. Save creates it.");
        _viewModel.BackupRoot.ShouldBeEmpty();
        _viewModel.SourcesRoot.ShouldBeEmpty();
        _viewModel.Exclude.ShouldBeEmpty();
        _viewModel.DiscoverAccessibleRepositories.ShouldBeFalse();
        _viewModel.Accounts.ShouldBeEmpty();
        _viewModel.Repositories.ShouldBeEmpty();
        _viewModel.IsDirty.ShouldBeFalse();
        _viewModel.SaveCommand.CanExecute(null).ShouldBeTrue();
        var record = _logger.Collector.GetSnapshot().Where(entry => entry.Id.Id == _backupConfigMissingEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Path").ShouldBe(_configPath);
        record.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task Save_NewConfigEmptyBackupRoot_ShowsErrorAtFieldAndWritesNothing()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new BackupConfigNotFoundException(_configPath));
        await LoadAsync();

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel, nameof(SettingsViewModel.BackupRoot)).ShouldBe(["backupRoot must be set."]);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
        _viewModel.IsNewConfig.ShouldBeTrue();
    }

    [Fact]
    public async Task Save_NewConfigFilled_SavesCurrentSchemaVersionAndNavigatesBack()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new BackupConfigNotFoundException(_configPath));
        await LoadAsync();
        _viewModel.BackupRoot = @"D:\Backups";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var saved = SavedConfig();
        saved.SchemaVersion.ShouldBe(BackupConfig.CurrentSchemaVersion);
        saved.BackupRoot.ShouldBe(@"D:\Backups");
        saved.SourcesRoot.ShouldBeNull();
        saved.Exclude.ShouldBeEmpty();
        saved.DiscoverAccessibleRepositories.ShouldBeFalse();
        saved.Accounts.ShouldBeEmpty();
        saved.Repositories.ShouldBeEmpty();
        _viewModel.IsNewConfig.ShouldBeFalse();
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
    }

    [Fact]
    public async Task Cancel_NewConfigNoChanges_NavigatesWithoutSaving()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new BackupConfigNotFoundException(_configPath));
        await LoadAsync();

        _viewModel.CancelCommand.Execute(null);

        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Save_EditedValues_PreservesHiddenFieldsAndNavigatesBack()
    {
        await LoadAsync();
        _viewModel.BackupRoot = @"  D:\NewBackups ";
        _viewModel.SourcesRoot = "";
        _viewModel.Exclude = "a-*, ,b-*";
        _viewModel.Accounts[0].Repositories[0].Branch = "dev";
        _viewModel.Accounts[0].Repositories[1].Path = "";
        _viewModel.Repositories[0].Path = @"F:\Lib";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var saved = SavedConfig();
        saved.SchemaVersion.ShouldBe(BackupConfig.CurrentSchemaVersion);
        saved.BackupRoot.ShouldBe(@"D:\NewBackups");
        saved.SourcesRoot.ShouldBeNull();
        saved.Exclude.ShouldBe(["a-*", "b-*"]);
        var tool = saved.Accounts[0].Repositories[0];
        (tool.Name, tool.Id, tool.Status, tool.Branch, tool.Path).ShouldBe(("tool", 101L, RepositoryStatus.Active, "dev", (string?)null));
        var old = saved.Accounts[0].Repositories[1];
        (old.Name, old.Id, old.Status, old.Branch, old.Path).ShouldBe(("old", 102L, RepositoryStatus.Unavailable, "legacy", (string?)null));
        var library = saved.Repositories.ShouldHaveSingleItem();
        (library.Url, library.Id, library.Status, library.Path).ShouldBe(("https://github.com/other/lib", 201L, RepositoryStatus.Active, @"F:\Lib"));
        _viewModel.IsDirty.ShouldBeFalse();
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Save_ToggledDiscoverAccessible_SavesValue(bool value)
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(new BackupConfigLoadResult(WithDiscover(CreateConfig(), !value), []));
        await LoadAsync();

        _viewModel.DiscoverAccessibleRepositories = value;
        _viewModel.IsDirty.ShouldBeTrue();
        await _viewModel.SaveCommand.ExecuteAsync(null);

        SavedConfig().DiscoverAccessibleRepositories.ShouldBe(value);
    }

    [Fact]
    public async Task Save_InvalidBackupRoot_ShowsErrorAtFieldAndWritesNothing()
    {
        await LoadAsync();
        _viewModel.BackupRoot = "relative";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel, nameof(SettingsViewModel.BackupRoot)).ShouldBe(["backupRoot must be an absolute path."]);
        _viewModel.GeneralErrors.ShouldBeEmpty();
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_InvalidRowValues_ShowsErrorsAtRowFields()
    {
        await LoadAsync();
        _viewModel.Accounts[0].Url = "https://example.com/octo";
        _viewModel.Accounts[0].Repositories[0].Branch = "-x";
        _viewModel.Repositories[0].Url = "https://github.com/other";
        _viewModel.Repositories[0].Path = "relative";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel.Accounts[0], nameof(AccountSettingsViewModel.Url))
            .ShouldBe(["accounts[0].url must be an account URL of the form https://github.com/<login>."]);
        ErrorsOf(_viewModel.Accounts[0].Repositories[0], nameof(AccountRepositorySettingsViewModel.Branch))
            .ShouldBe(["accounts[0].repositories[0].branch must be null or a branch name that is not empty and does not start with '-'."]);
        ErrorsOf(_viewModel.Repositories[0], nameof(RepositorySettingsViewModel.Url))
            .ShouldBe(["repositories[0].url must be a repository URL of the form https://github.com/<owner>/<repo>."]);
        ErrorsOf(_viewModel.Repositories[0], nameof(RepositorySettingsViewModel.Path))
            .ShouldBe(["repositories[0].path must be an absolute path."]);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Save_ErrorOfHiddenProperty_ShowsAtPathOfEntry()
    {
        await LoadAsync();
        _viewModel.AddRepositoryCommand.Execute(null);
        _viewModel.Repositories[1].Url = "https://github.com/octo/tool";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel.Repositories[1], nameof(RepositorySettingsViewModel.Url))
            .ShouldBe(["repositories[1].url names the same repository as accounts[0].repositories[0].name."]);
        ErrorsOf(_viewModel.Repositories[1], nameof(RepositorySettingsViewModel.Path))
            .ShouldBe([@"repositories[1] has the same archive folder 'D:\Backups\octo\tool' as accounts[0].repositories[0]."]);
    }

    [Fact]
    public async Task EditField_AfterReportedError_ClearsError()
    {
        await LoadAsync();
        _viewModel.Accounts[0].Url = "https://example.com/octo";
        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.Accounts[0].Url = "https://github.com/octo2";

        _viewModel.Accounts[0].HasErrors.ShouldBeFalse();
    }

    [Theory]
    [InlineData("relative", SettingsFormViewModel.AbsolutePathMessage)]
    [InlineData("", SettingsFormViewModel.RequiredPathMessage)]
    public async Task EditBackupRoot_InvalidValue_ShowsHintAtOnce(string value, string expected)
    {
        await LoadAsync();

        _viewModel.BackupRoot = value;

        ErrorsOf(_viewModel, nameof(SettingsViewModel.BackupRoot)).ShouldBe([expected]);
    }

    [Fact]
    public async Task EditBranch_LeadingDash_ShowsHintAtOnce()
    {
        await LoadAsync();

        _viewModel.Repositories[0].Branch = "-D";

        ErrorsOf(_viewModel.Repositories[0], nameof(RepositorySettingsViewModel.Branch)).ShouldBe([SettingsFormViewModel.BranchMessage]);
    }

    [Fact]
    public async Task Save_SchemaError_ShowsGeneralError()
    {
        var config = CreateConfig();
        config.SchemaVersion = 2;
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(config, []));
        await LoadAsync();
        _viewModel.BackupRoot = @"D:\NewBackups";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.GeneralErrors.ShouldBe(["schemaVersion 2 is not supported; the supported version is 1."]);
        _viewModel.HasGeneralErrors.ShouldBeTrue();
    }

    [Fact]
    public async Task EditSourcesRoot_InsideBackupRoot_ShowsWarningBeforeSave()
    {
        await LoadAsync();

        _viewModel.SourcesRoot = @"D:\Backups\clones";

        _viewModel.Warnings.ShouldHaveSingleItem().ShouldStartWith(@"sourcesRoot 'D:\Backups\clones' is inside backupRoot");
        _viewModel.HasWarnings.ShouldBeTrue();
    }

    [Fact]
    public async Task Save_SourcesRootInsideBackupRoot_ShowsWarningAndSaves()
    {
        await LoadAsync();
        _viewModel.SourcesRoot = @"D:\Backups\clones";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.Warnings.ShouldHaveSingleItem().ShouldContain("is inside backupRoot");
        SavedConfig().SourcesRoot.ShouldBe(@"D:\Backups\clones");
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_WriteFails_ShowsMaskedDialogAndKeepsFields(bool unauthorized)
    {
        _masker.Register(_token);
        await LoadAsync();
        _viewModel.BackupRoot = @"D:\NewBackups";
        Exception failure = unauthorized
            ? new UnauthorizedAccessException($"Access denied {_token}")
            : new IOException($"Disk full {_token}");
        _configStore.SaveAsync(Arg.Any<BackupConfig>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowError(SettingsViewModel.SaveFailedTitle, Arg.Is<string>(text => text.EndsWith(" ***") && !text.Contains(_token)));
        _viewModel.BackupRoot.ShouldBe(@"D:\NewBackups");
        _viewModel.IsDirty.ShouldBeTrue();
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
        AssertTokenNotLogged();
    }

    [Fact]
    public async Task Cancel_NoChanges_NavigatesWithoutAsking()
    {
        await LoadAsync();

        _viewModel.CancelCommand.Execute(null);

        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancel_UnsavedChanges_AsksAndWritesNothing(bool confirmed)
    {
        await LoadAsync();
        _viewModel.Accounts[0].Repositories[0].Path = @"F:\X";
        _dialogs.Confirm(SettingsViewModel.DiscardTitle, SettingsViewModel.DiscardMessage).Returns(confirmed);

        _viewModel.CancelCommand.Execute(null);

        _dialogs.Received(1).Confirm(SettingsViewModel.DiscardTitle, SettingsViewModel.DiscardMessage);
        _navigation.Received(confirmed ? 1 : 0).NavigateTo<RepositoriesViewModel>();
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
        await _toolPathsStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
        await _tokenStore.DidNotReceiveWithAnyArgs().WriteAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanCloseAsync_Dirty_ReturnsAnswerOfUser(bool confirmed)
    {
        await LoadAsync();
        _viewModel.GitPath = @"C:\Git\git.exe";
        _dialogs.Confirm(SettingsViewModel.DiscardTitle, SettingsViewModel.DiscardMessage).Returns(confirmed);

        var canClose = await _viewModel.CanCloseAsync();

        canClose.ShouldBe(confirmed);
    }

    [Fact]
    public async Task CanCloseAsync_TypedToken_Asks()
    {
        await LoadAsync();
        _viewModel.Token = _typedToken;

        var canClose = await _viewModel.CanCloseAsync();

        canClose.ShouldBeFalse();
        _dialogs.Received(1).Confirm(SettingsViewModel.DiscardTitle, SettingsViewModel.DiscardMessage);
    }

    [Fact]
    public async Task CanCloseAsync_NoChanges_ReturnsTrueWithoutAsking()
    {
        await LoadAsync();

        (await _viewModel.CanCloseAsync()).ShouldBeTrue();

        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
    }

    [Fact]
    public async Task AddRepository_Save_WritesNewEntryWithUnknownIdAndActiveStatus()
    {
        await LoadAsync();
        _viewModel.AddRepositoryCommand.Execute(null);
        _viewModel.Repositories[1].Url = "https://github.com/new/repo";
        _viewModel.Repositories[1].Branch = "main";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var added = SavedConfig().Repositories[1];
        (added.Url, added.Id, added.Status, added.Branch, added.Path).ShouldBe(("https://github.com/new/repo", 0L, RepositoryStatus.Active, "main", (string?)null));
    }

    [Fact]
    public async Task AddAccount_Save_WritesNewAccountWithoutRepositories()
    {
        await LoadAsync();
        _viewModel.AddAccountCommand.Execute(null);
        _viewModel.Accounts[1].Url = "https://github.com/acme";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var added = SavedConfig().Accounts[1];
        added.Url.ShouldBe("https://github.com/acme");
        added.Repositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveEntries_Save_WritesWithoutThem()
    {
        await LoadAsync();
        var account = _viewModel.Accounts[0];

        account.RemoveRepositoryCommand.Execute(account.Repositories[1]);
        _viewModel.RemoveRepositoryCommand.Execute(_viewModel.Repositories[0]);
        await _viewModel.SaveCommand.ExecuteAsync(null);

        var saved = SavedConfig();
        saved.Accounts[0].Repositories.Select(repository => repository.Name).ShouldBe(["tool"]);
        saved.Repositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveAccount_MarksDirtyAndSavesWithoutIt()
    {
        await LoadAsync();

        _viewModel.RemoveAccountCommand.Execute(_viewModel.Accounts[0]);

        _viewModel.IsDirty.ShouldBeTrue();
        await _viewModel.SaveCommand.ExecuteAsync(null);
        SavedConfig().Accounts.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_TelemetrySettings_FillsFieldsWithoutChanges()
    {
        _viewModel.TelemetryEnabled.ShouldBeTrue();
        _viewModel.OtlpEndpoint.ShouldBe("http://localhost:4317");
        _viewModel.HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task Save_ToolsChanged_WritesOnlyToolsAndNavigatesBack()
    {
        await LoadAsync();
        _viewModel.GitPath = @" C:\Git\git.exe ";
        _viewModel.SevenZipPath = "";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _toolPathsStore.Received(1).SaveAsync(new ToolPaths(@"C:\Git\git.exe", null), Arg.Any<CancellationToken>());
        await AssertOnlyWrittenAsync(tools: true);
        _viewModel.HasUnsavedChanges.ShouldBeFalse();
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
    }

    [Fact]
    public async Task Save_TelemetryChanged_WritesOnlyTelemetryAndNavigatesBack()
    {
        await LoadAsync();
        _viewModel.TelemetryEnabled = false;
        _viewModel.OtlpEndpoint = " http://otel:4317 ";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _telemetryStore.Received(1).SaveAsync(new TelemetrySettings(false, "http://otel:4317", _fileHeaders), Arg.Any<CancellationToken>());
        await AssertOnlyWrittenAsync(telemetry: true);
        _viewModel.HasUnsavedChanges.ShouldBeFalse();
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_BlankEndpoint_WritesNull()
    {
        await LoadAsync();
        _viewModel.OtlpEndpoint = "  ";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _telemetryStore.Received(1).SaveAsync(new TelemetrySettings(true, null, _fileHeaders), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_TokenTyped_WritesTrimmedTokenAndClearsField()
    {
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new GitHubToken(_typedToken, _storeLocation));
        await LoadAsync();
        _viewModel.Token = $"  {_typedToken} ";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _tokenStore.Received(1).WriteAsync(_typedToken, Arg.Any<CancellationToken>());
        await AssertOnlyWrittenAsync(token: true);
        _viewModel.Token.ShouldBeEmpty();
        _viewModel.TokenError.ShouldBeNull();
        _dialogs.DidNotReceiveWithAnyArgs().ShowInformation(default!, default!);
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
        _masker.Mask(_typedToken).ShouldBe("***");
    }

    [Fact]
    public async Task Save_AllGroupsChanged_WritesConfigurationToolsTelemetryAndTokenInOrder()
    {
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new GitHubToken(_typedToken, _storeLocation));
        await LoadAsync();
        _viewModel.BackupRoot = @"D:\NewBackups";
        _viewModel.GitPath = @"C:\Git\git.exe";
        _viewModel.OtlpEndpoint = "http://otel:4317";
        _viewModel.Token = _typedToken;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Received.InOrder(() =>
        {
            _ = _configStore.SaveAsync(Arg.Any<BackupConfig>(), Arg.Any<CancellationToken>());
            _ = _toolPathsStore.SaveAsync(Arg.Any<ToolPaths>(), Arg.Any<CancellationToken>());
            _ = _telemetryStore.SaveAsync(Arg.Any<TelemetrySettings>(), Arg.Any<CancellationToken>());
            _ = _tokenStore.WriteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
        _viewModel.HasUnsavedChanges.ShouldBeFalse();
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_NothingChanged_WritesNothingAndNavigatesBack()
    {
        await LoadAsync();

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await AssertOnlyWrittenAsync();
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
        _dialogs.DidNotReceiveWithAnyArgs().Confirm(default!, default!);
    }

    [Fact]
    public async Task Save_ConfigurationTokenTakesPriority_ShowsInformationWithSourceName()
    {
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new GitHubToken(_token, _configurationSource));
        await LoadAsync();
        _viewModel.Token = _typedToken;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowInformation(
            SettingsViewModel.ShadowedTokenTitle,
            Arg.Is<string>(text => text.Contains(_configurationSource) && !text.Contains(_token) && !text.Contains(_typedToken)));
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_EffectiveTokenCannotBeRead_SkipsAdviceAndNavigatesBack()
    {
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new TokenStoreException($"Cannot read {_typedToken} from the store."));
        await LoadAsync();
        _viewModel.Token = _typedToken;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _tokenStore.Received(1).WriteAsync(_typedToken, Arg.Any<CancellationToken>());
        _dialogs.DidNotReceiveWithAnyArgs().ShowInformation(default!, default!);
        _dialogs.DidNotReceiveWithAnyArgs().ShowError(default!, default!);
        _logger.Collector.GetSnapshot().ShouldAllBe(entry => entry.Exception == null && !entry.Message.Contains(_typedToken));
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_WhitespaceToken_ShowsFieldErrorAndWritesNothing()
    {
        await LoadAsync();
        _viewModel.Token = "   ";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.TokenError.ShouldBe(SettingsViewModel.BlankTokenMessage);
        await AssertOnlyWrittenAsync();
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_InvalidTools_ShowsErrorAtFieldAndWritesNothing()
    {
        _toolPathsStore.Validate(Arg.Any<ToolPaths>()).Returns(["Tools:GitPath must be an absolute path when set."]);
        await LoadAsync();
        _viewModel.BackupRoot = @"D:\NewBackups";
        _viewModel.GitPath = "git.exe";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel, nameof(SettingsViewModel.GitPath)).ShouldBe(["Tools:GitPath must be an absolute path when set."]);
        await AssertOnlyWrittenAsync();
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_InvalidTelemetry_ShowsErrorAtFieldAndWritesNothing()
    {
        const string error = "OpenTelemetry:OtlpEndpoint must be an absolute URI when OpenTelemetry:Enabled is true.";
        _telemetryStore.Validate(Arg.Any<TelemetrySettings>()).Returns([error]);
        await LoadAsync();
        _viewModel.OtlpEndpoint = "localhost";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel, nameof(SettingsViewModel.OtlpEndpoint)).ShouldBe([error]);
        await AssertOnlyWrittenAsync();
    }

    [Fact]
    public async Task Save_InvalidConfigurationAndValidTools_WritesNothing()
    {
        await LoadAsync();
        _viewModel.BackupRoot = "relative";
        _viewModel.GitPath = @"C:\Git\git.exe";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel, nameof(SettingsViewModel.BackupRoot)).ShouldNotBeEmpty();
        await AssertOnlyWrittenAsync();
    }

    [Fact]
    public async Task Save_ErrorsInSeveralGroups_ShowsAllOfThem()
    {
        _toolPathsStore.Validate(Arg.Any<ToolPaths>()).Returns(["Tools:GitPath must be an absolute path when set."]);
        _telemetryStore.Validate(Arg.Any<TelemetrySettings>()).Returns(["OpenTelemetry:OtlpEndpoint must be an absolute URI."]);
        await LoadAsync();
        _viewModel.GitPath = "git.exe";
        _viewModel.OtlpEndpoint = "localhost";
        _viewModel.Token = "   ";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel, nameof(SettingsViewModel.GitPath)).ShouldHaveSingleItem();
        ErrorsOf(_viewModel, nameof(SettingsViewModel.OtlpEndpoint)).ShouldHaveSingleItem();
        _viewModel.TokenError.ShouldBe(SettingsViewModel.BlankTokenMessage);
        await AssertOnlyWrittenAsync();
    }

    [Fact]
    public async Task Save_UnchangedGroupIsInvalid_DoesNotValidateIt()
    {
        _toolPathsStore.Validate(Arg.Any<ToolPaths>()).Returns(["Tools:GitPath must be an absolute path when set."]);
        _telemetryStore.Validate(Arg.Any<TelemetrySettings>()).Returns(["OpenTelemetry:OtlpEndpoint must be an absolute URI."]);
        await LoadAsync();
        _viewModel.BackupRoot = @"D:\NewBackups";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await AssertOnlyWrittenAsync(config: true);
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_ToolsAndTelemetryErrorsWithoutField_ShowsGeneralErrors()
    {
        _toolPathsStore.Validate(Arg.Any<ToolPaths>()).Returns(["Tools: unknown problem."]);
        _telemetryStore.Validate(Arg.Any<TelemetrySettings>()).Returns(["OpenTelemetry: other problem."]);
        await LoadAsync();
        _viewModel.GitPath = @"C:\Git\git.exe";
        _viewModel.TelemetryEnabled = false;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.GeneralErrors.ShouldBe(["Tools: unknown problem.", "OpenTelemetry: other problem."]);
        await AssertOnlyWrittenAsync();
    }

    [Theory]
    [InlineData("config")]
    [InlineData("tools")]
    [InlineData("telemetry")]
    public async Task Save_WriteFails_ShowsDialogKeepsEditorOpenAndFields(string group)
    {
        await LoadAsync();
        _viewModel.BackupRoot = @"D:\NewBackups";
        _viewModel.GitPath = @"C:\Git\git.exe";
        _viewModel.OtlpEndpoint = "http://otel:4317";
        var failure = new IOException("Locked");
        switch (group)
        {
            case "config":
                _configStore.SaveAsync(Arg.Any<BackupConfig>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
                break;
            case "tools":
                _toolPathsStore.SaveAsync(Arg.Any<ToolPaths>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
                break;
            default:
                _telemetryStore.SaveAsync(Arg.Any<TelemetrySettings>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
                break;
        }

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowError(SettingsViewModel.SaveFailedTitle, "Locked");
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
        _viewModel.BackupRoot.ShouldBe(@"D:\NewBackups");
        _viewModel.GitPath.ShouldBe(@"C:\Git\git.exe");
        _viewModel.OtlpEndpoint.ShouldBe("http://otel:4317");
        _viewModel.HasUnsavedChanges.ShouldBeTrue();
    }

    [Fact]
    public async Task Save_TokenStoreFails_ShowsMaskedDialogKeepsTokenAndDoesNotLogIt()
    {
        _tokenStore.WriteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TokenStoreException($"Cannot write {_typedToken} to the store."));
        await LoadAsync();
        _viewModel.Token = _typedToken;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowError(SettingsViewModel.SaveFailedTitle, "Cannot write *** to the store.");
        _viewModel.Token.ShouldBe(_typedToken);
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_RetryAfterPartialFailure_WritesOnlyTheGroupsStillUnsaved()
    {
        await LoadAsync();
        _viewModel.BackupRoot = @"D:\NewBackups";
        _viewModel.GitPath = @"C:\Git\git.exe";
        _viewModel.OtlpEndpoint = "http://otel:4317";
        _toolPathsStore.SaveAsync(Arg.Any<ToolPaths>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("Locked"));
        await _viewModel.SaveCommand.ExecuteAsync(null);
        _toolPathsStore.SaveAsync(Arg.Any<ToolPaths>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _configStore.Received(1).SaveAsync(Arg.Any<BackupConfig>(), Arg.Any<CancellationToken>());
        await _toolPathsStore.Received(2).SaveAsync(Arg.Any<ToolPaths>(), Arg.Any<CancellationToken>());
        await _telemetryStore.Received(1).SaveAsync(Arg.Any<TelemetrySettings>(), Arg.Any<CancellationToken>());
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_ConfigNotReadable_IsAvailableAndWritesOnlyChangedOtherGroups()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(
            new BackupConfigException(_configPath, ["backupRoot must be set."]));
        await LoadAsync();
        _viewModel.SaveCommand.CanExecute(null).ShouldBeTrue();
        _viewModel.GitPath = @"C:\Git\git.exe";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await AssertOnlyWrittenAsync(tools: true);
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public void Save_BeforeLoad_CannotExecute() =>
        _viewModel.SaveCommand.CanExecute(null).ShouldBeFalse();

    [Fact]
    public async Task Cancel_UnsavedTelemetry_Asks()
    {
        await LoadAsync();
        _viewModel.TelemetryEnabled = false;

        _viewModel.CancelCommand.Execute(null);

        _dialogs.Received(1).Confirm(SettingsViewModel.DiscardTitle, SettingsViewModel.DiscardMessage);
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public void Constructor_TelemetryHeaders_FillsHiddenFieldWithoutChanges()
    {
        _viewModel.TelemetryHeaders.ShouldBe(_fileHeaders);
        _viewModel.IsTelemetryHeadersShown.ShouldBeFalse();
        _viewModel.HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_HeadersOverridden_ShowsNote()
    {
        _telemetryStore.HeadersOverridden.Returns(true);

        CreateViewModel().IsTelemetryHeadersOverridden.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_NotOverridden_HidesNote() =>
        _viewModel.IsTelemetryHeadersOverridden.ShouldBeFalse();

    [Fact]
    public void HeadersOverriddenMessage_NamesKeyAndSources()
    {
        SettingsViewModel.HeadersOverriddenMessage.ShouldStartWith(TelemetrySettings.HeadersKey);
        SettingsViewModel.HeadersOverriddenMessage.ShouldContain("environment variable");
    }

    [Fact]
    public async Task EditTelemetryHeaders_MarksUnsavedChanges()
    {
        await LoadAsync();

        _viewModel.TelemetryHeaders = "api-key=new-secret";

        _viewModel.HasUnsavedChanges.ShouldBeTrue();
    }

    [Fact]
    public async Task ToggleShown_DoesNotMarkUnsavedChanges()
    {
        await LoadAsync();

        _viewModel.IsTelemetryHeadersShown = true;
        _viewModel.IsTelemetryHeadersShown = false;

        _viewModel.HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task Save_HeadersChanged_WritesTelemetryWithHeadersAndNavigatesBack()
    {
        await LoadAsync();
        _viewModel.TelemetryHeaders = " api-key=new-secret ";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _telemetryStore.Received(1).SaveAsync(
            new TelemetrySettings(true, "http://localhost:4317", "api-key=new-secret"),
            Arg.Any<CancellationToken>());
        await AssertOnlyWrittenAsync(telemetry: true);
        _viewModel.HasUnsavedChanges.ShouldBeFalse();
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_BlankHeaders_WritesNullHeaders()
    {
        await LoadAsync();
        _viewModel.TelemetryHeaders = "   ";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _telemetryStore.Received(1).SaveAsync(
            new TelemetrySettings(true, "http://localhost:4317", null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_OnlyEnabledChanged_WritesFileHeadersUnchanged()
    {
        await LoadAsync();
        _viewModel.TelemetryEnabled = false;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _telemetryStore.Received(1).SaveAsync(
            new TelemetrySettings(false, "http://localhost:4317", _fileHeaders),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_HeadersError_ShowsErrorAtHeadersFieldAndWritesNothing()
    {
        const string error = "OpenTelemetry:Headers must be a comma-separated list of name=value pairs with a header name and a value in each pair.";
        _telemetryStore.Validate(Arg.Any<TelemetrySettings>()).Returns([error]);
        await LoadAsync();
        _viewModel.TelemetryHeaders = "malformed secret";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel, nameof(SettingsViewModel.TelemetryHeaders)).ShouldBe([error]);
        ErrorsOf(_viewModel, nameof(SettingsViewModel.OtlpEndpoint)).ShouldBeEmpty();
        _viewModel.GeneralErrors.ShouldBeEmpty();
        await AssertOnlyWrittenAsync();
    }

    [Fact]
    public async Task Save_EndpointErrorMentioningHeaders_ShowsErrorAtEndpointField()
    {
        const string error = "OpenTelemetry:OtlpEndpoint must be an absolute https URI, or http for a loopback address, when OpenTelemetry:Headers is set.";
        _telemetryStore.Validate(Arg.Any<TelemetrySettings>()).Returns([error]);
        await LoadAsync();
        _viewModel.OtlpEndpoint = "http://collector.example:4317";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        ErrorsOf(_viewModel, nameof(SettingsViewModel.OtlpEndpoint)).ShouldBe([error]);
        ErrorsOf(_viewModel, nameof(SettingsViewModel.TelemetryHeaders)).ShouldBeEmpty();
        await AssertOnlyWrittenAsync();
    }

    [Fact]
    public async Task Save_TelemetryWriteFails_ShowsMaskedDialogAndKeepsHeaders()
    {
        _telemetryStore.SaveAsync(Arg.Any<TelemetrySettings>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _masker.Register("new-secret");
                return Task.FromException(new IOException("Cannot write new-secret to appsettings.json."));
            });
        await LoadAsync();
        _viewModel.TelemetryHeaders = "api-key=new-secret";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowError(SettingsViewModel.SaveFailedTitle, "Cannot write *** to appsettings.json.");
        _viewModel.TelemetryHeaders.ShouldBe("api-key=new-secret");
        _viewModel.HasUnsavedChanges.ShouldBeTrue();
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Cancel_UnsavedHeaders_Asks()
    {
        await LoadAsync();
        _viewModel.TelemetryHeaders = "api-key=new-secret";

        _viewModel.CancelCommand.Execute(null);

        _dialogs.Received(1).Confirm(SettingsViewModel.DiscardTitle, SettingsViewModel.DiscardMessage);
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Reopen_AfterSave_ShowsSavedHeadersHidden()
    {
        await LoadAsync();
        _viewModel.IsTelemetryHeadersShown = true;
        _viewModel.TelemetryHeaders = "api-key=new-secret";
        await _viewModel.SaveCommand.ExecuteAsync(null);
        _telemetryStore.Current.Returns(new TelemetrySettings(true, "http://localhost:4317", "api-key=new-secret"));

        var reopened = CreateViewModel();

        reopened.TelemetryHeaders.ShouldBe("api-key=new-secret");
        reopened.IsTelemetryHeadersShown.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_HeadersInAppSettings_ShowsNoteAndMarksTelemetryChanged()
    {
        _telemetryStore.HeadersInAppSettings.Returns(true);

        var viewModel = CreateViewModel();

        viewModel.IsTelemetryHeadersInAppSettings.ShouldBeTrue();
        viewModel.HasUnsavedChanges.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_HeadersNotInAppSettings_HidesNoteAndLeavesTelemetryUnchanged()
    {
        _viewModel.IsTelemetryHeadersInAppSettings.ShouldBeFalse();
        _viewModel.HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public void HeadersMessages_NameKeyAndCredentialManager()
    {
        SettingsViewModel.HeadersOverriddenMessage.ShouldContain("Windows Credential Manager");
        SettingsViewModel.HeadersInAppSettingsMessage.ShouldStartWith(TelemetrySettings.HeadersKey);
        SettingsViewModel.HeadersInAppSettingsMessage.ShouldContain("Windows Credential Manager");
        SettingsViewModel.HeadersMovedMessage.ShouldContain("appsettings.json.bak");
    }

    [Fact]
    public async Task Save_HeadersInAppSettings_SavesTelemetryAndReportsBackup()
    {
        _telemetryStore.HeadersInAppSettings.Returns(true);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.SaveCommand.ExecuteAsync(null);

        await _telemetryStore.Received(1).SaveAsync(
            new TelemetrySettings(true, "http://localhost:4317", _fileHeaders), Arg.Any<CancellationToken>());
        _dialogs.Received(1).ShowInformation(SettingsViewModel.HeadersMovedTitle, SettingsViewModel.HeadersMovedMessage);
        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public async Task Save_HeadersNotInAppSettings_DoesNotReportBackup()
    {
        await LoadAsync();
        _viewModel.TelemetryHeaders = "api-key=new-secret";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _dialogs.DidNotReceive().ShowInformation(SettingsViewModel.HeadersMovedTitle, Arg.Any<string>());
    }

    [Fact]
    public async Task Save_HeadersStoreFails_ShowsMaskedDialogAndStaysOpen()
    {
        _telemetryStore.SaveAsync(Arg.Any<TelemetrySettings>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _masker.Register("new-secret");
                return Task.FromException(
                    new OtlpHeadersStoreException("Cannot save the credential 'target': new-secret is too long."));
            });
        await LoadAsync();
        _viewModel.TelemetryHeaders = "api-key=new-secret";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowError(
            SettingsViewModel.SaveFailedTitle, "Cannot save the credential 'target': *** is too long.");
        _viewModel.HasUnsavedChanges.ShouldBeTrue();
        _navigation.DidNotReceive().NavigateTo<RepositoriesViewModel>();
    }

    /// <summary>
    /// Creates another editor over the same substitutes, as opening the editor again does.
    /// </summary>
    /// <returns>The editor.</returns>
    private SettingsViewModel CreateViewModel() => new(
        _configStore, _toolPathsStore, _telemetryStore, _tokenStore, _tokenProvider, _inspector, _navigation, _dialogs, _masker,
        TimeProvider.System, _logger, _preferences);

    /// <summary>
    /// Asserts that exactly the given groups were written and the others never were.
    /// </summary>
    private async Task AssertOnlyWrittenAsync(bool config = false, bool tools = false, bool telemetry = false, bool token = false)
    {
        await _configStore.Received(config ? 1 : 0).SaveAsync(Arg.Any<BackupConfig>(), Arg.Any<CancellationToken>());
        await _toolPathsStore.Received(tools ? 1 : 0).SaveAsync(Arg.Any<ToolPaths>(), Arg.Any<CancellationToken>());
        await _telemetryStore.Received(telemetry ? 1 : 0).SaveAsync(Arg.Any<TelemetrySettings>(), Arg.Any<CancellationToken>());
        await _tokenStore.Received(token ? 1 : 0).WriteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckToken_TypedClassicToken_ShowsLoginScopesAndRateLimit()
    {
        _inspector.InspectAsync(_typedToken, Arg.Any<CancellationToken>())
            .Returns(new GitHubTokenInfo("octocat", ["repo", "read:org"], new(5000, 4990, _resetAt)));
        _viewModel.Token = _typedToken;

        await _viewModel.CheckTokenCommand.ExecuteAsync(null);

        _viewModel.TokenCheckLines.ShouldBe(
        [
            $"Token: {SettingsViewModel.TypedTokenSource}",
            "Signed in as octocat",
            "Scopes: repo, read:org",
            "Rate limit: 4990 of 5000 requests left, resets at 2026-11-14 13:30",
        ]);
        _viewModel.TokenCheckError.ShouldBeNull();
        await _tokenProvider.DidNotReceiveWithAnyArgs().GetTokenAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CheckToken_NoScopesNoRateLimit_ShowsFineGrainedHintAndNotReported()
    {
        _inspector.InspectAsync(_typedToken, Arg.Any<CancellationToken>())
            .Returns(new GitHubTokenInfo("octocat", null, null));
        _viewModel.Token = _typedToken;

        await _viewModel.CheckTokenCommand.ExecuteAsync(null);

        _viewModel.TokenCheckLines.ShouldBe(
        [
            $"Token: {SettingsViewModel.TypedTokenSource}",
            "Signed in as octocat",
            SettingsViewModel.ScopesNotReportedLine,
            "Rate limit: not reported",
        ]);
        _viewModel.TokenCheckError.ShouldBeNull();
    }

    [Fact]
    public async Task CheckToken_EmptyField_ChecksEffectiveTokenAndShowsItsSource()
    {
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new GitHubToken(_token, _configurationSource));
        _inspector.InspectAsync(_token, Arg.Any<CancellationToken>())
            .Returns(new GitHubTokenInfo("octocat", null, new(5000, 5000, _resetAt)));

        await _viewModel.CheckTokenCommand.ExecuteAsync(null);

        _viewModel.TokenCheckLines[0].ShouldBe($"Token: {_configurationSource}");
        _viewModel.TokenCheckLines[2].ShouldBe(SettingsViewModel.ScopesNotReportedLine);
    }

    [Fact]
    public async Task CheckToken_Anonymous_ShowsOnlyRateLimit()
    {
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(GitHubToken.Anonymous);
        _inspector.InspectAsync(null, Arg.Any<CancellationToken>()).Returns(new GitHubTokenInfo(null, null, new(60, 58, _resetAt)));

        await _viewModel.CheckTokenCommand.ExecuteAsync(null);

        _viewModel.TokenCheckLines.ShouldBe(
        [
            "Token: anonymous",
            SettingsViewModel.AnonymousLine,
            "Rate limit: 58 of 60 requests left, resets at 2026-11-14 13:30",
        ]);
    }

    [Fact]
    public async Task CheckToken_Rejected_ShowsInvalidOrExpired()
    {
        _inspector.InspectAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GitHubAuthenticationException("GitHub rejected the credentials (HTTP 401) for user.", "user"));
        _viewModel.Token = _typedToken;

        await _viewModel.CheckTokenCommand.ExecuteAsync(null);

        _viewModel.TokenCheckError.ShouldBe(SettingsViewModel.TokenRejectedMessage);
        _viewModel.TokenCheckLines.ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckToken_OtherApiError_ShowsMaskedMessage()
    {
        _inspector.InspectAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GitHubApiException($"The GitHub API request failed (HTTP 502) for user {_typedToken}.", "user", 502));
        _viewModel.Token = _typedToken;

        await _viewModel.CheckTokenCommand.ExecuteAsync(null);

        _viewModel.TokenCheckError.ShouldBe("The GitHub API request failed (HTTP 502) for user ***.");
    }

    [Fact]
    public async Task CheckToken_Cancelled_ShowsCancelled()
    {
        var pending = new TaskCompletionSource<GitHubTokenInfo>();
        _inspector.InspectAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<CancellationToken>().Register(() => pending.TrySetCanceled());
            return pending.Task;
        });
        _viewModel.Token = _typedToken;

        var check = _viewModel.CheckTokenCommand.ExecuteAsync(null);
        _viewModel.CheckTokenCancelCommand.Execute(null);
        await check;

        _viewModel.TokenCheckError.ShouldBe(SettingsViewModel.TokenCheckCancelledMessage);
    }

    [Fact]
    public void Constructor_SelectsGeneralAndListsSectionsInNavigationOrder()
    {
        _viewModel.SelectedSection.ShouldBe(SettingsSection.General);
        _viewModel.Sections.Select(section => section.Section).ShouldBe(
        [
            SettingsSection.General, SettingsSection.Accounts, SettingsSection.Repositories,
            SettingsSection.Tools, SettingsSection.Telemetry, SettingsSection.Token,
        ]);
        _viewModel.Sections.Select(section => section.Title)
            .ShouldBe(["General", "Accounts", "Repositories", "Tools", "Telemetry", "GitHub token"]);
        _viewModel.Sections.Select(section => section.AutomationId).ShouldBe(
        [
            "SettingsSectionGeneral", "SettingsSectionAccounts", "SettingsSectionRepositories",
            "SettingsSectionTools", "SettingsSectionTelemetry", "SettingsSectionToken",
        ]);
        _viewModel.Sections.Where(section => section.IsSelected).ShouldHaveSingleItem().ShouldBe(_viewModel.GeneralSection);
        _viewModel.Sections.ShouldAllBe(section => !section.HasErrors);
    }

    [Fact]
    public void Sections_EachHasItsOwnGlyph()
    {
        _viewModel.Sections.ShouldAllBe(section => !string.IsNullOrWhiteSpace(section.Glyph));
        _viewModel.Sections.Select(section => section.Glyph).Distinct().Count().ShouldBe(_viewModel.Sections.Count);
    }

    [Fact]
    public void SelectedSection_Changed_SelectsOnlyItsEntry()
    {
        _viewModel.SelectedSection = SettingsSection.Telemetry;

        _viewModel.Sections.Where(section => section.IsSelected).ShouldHaveSingleItem().ShouldBe(_viewModel.TelemetrySection);
    }

    [Fact]
    public async Task Load_Accounts_SelectsFirstAccount()
    {
        await LoadAccountsAsync("a", "b");

        _viewModel.SelectedAccount.ShouldBe(_viewModel.Accounts[0]);
        _viewModel.HasSelectedAccount.ShouldBeTrue();
    }

    [Fact]
    public async Task Load_NoAccounts_SelectsNone()
    {
        await LoadAccountsAsync();

        _viewModel.SelectedAccount.ShouldBeNull();
        _viewModel.HasSelectedAccount.ShouldBeFalse();
    }

    [Fact]
    public async Task AddAccount_SelectsNewAccountNamedAsNew()
    {
        await LoadAsync();

        _viewModel.AddAccountCommand.Execute(null);

        _viewModel.SelectedAccount.ShouldBe(_viewModel.Accounts[1]);
        _viewModel.Accounts[1].DisplayName.ShouldBe(AccountSettingsViewModel.NewAccountName);
    }

    [Fact]
    public async Task DisplayName_Url_IsTrimmedUrlAndFollowsEdits()
    {
        await LoadAsync();
        var account = _viewModel.Accounts[0];
        var notified = new List<string?>();
        account.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        account.Url = "  https://github.com/other  ";

        account.DisplayName.ShouldBe("https://github.com/other");
        notified.ShouldContain(nameof(AccountSettingsViewModel.DisplayName));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public async Task RemoveAccount_Selected_SelectsNextOrPrevious(int removed, int expected)
    {
        await LoadAccountsAsync("a", "b", "c");
        var accounts = _viewModel.Accounts.ToArray();
        _viewModel.SelectedAccount = accounts[removed];

        _viewModel.RemoveAccountCommand.Execute(accounts[removed]);

        _viewModel.SelectedAccount.ShouldBe(accounts[expected]);
    }

    [Fact]
    public async Task RemoveAccount_OnlyAccount_ClearsSelection()
    {
        await LoadAsync();

        _viewModel.RemoveAccountCommand.Execute(_viewModel.Accounts[0]);

        _viewModel.SelectedAccount.ShouldBeNull();
        _viewModel.HasSelectedAccount.ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveAccount_NotSelected_KeepsSelection()
    {
        await LoadAccountsAsync("a", "b", "c");
        var selected = _viewModel.Accounts[0];

        _viewModel.RemoveAccountCommand.Execute(_viewModel.Accounts[2]);

        _viewModel.SelectedAccount.ShouldBe(selected);
    }

    [Fact]
    public async Task EditBackupRoot_InvalidThenValid_MarksAndUnmarksGeneral()
    {
        await LoadAsync();

        _viewModel.BackupRoot = "relative";
        var marked = _viewModel.GeneralSection.HasErrors;
        _viewModel.BackupRoot = @"D:\Valid";

        marked.ShouldBeTrue();
        _viewModel.GeneralSection.HasErrors.ShouldBeFalse();
        _viewModel.SelectedSection.ShouldBe(SettingsSection.General);
    }

    [Fact]
    public async Task Save_AccountUrlError_SelectsAccountsAndFirstAccountWithError()
    {
        await LoadAccountsAsync("a", "b", "c");
        _viewModel.Accounts[1].Url = "https://example.com/b";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.SelectedSection.ShouldBe(SettingsSection.Accounts);
        _viewModel.AccountsSection.HasErrors.ShouldBeTrue();
        _viewModel.AccountsSection.IsSelected.ShouldBeTrue();
        _viewModel.SelectedAccount.ShouldBe(_viewModel.Accounts[1]);
        _viewModel.Accounts.Select(account => account.HasAnyErrors).ShouldBe([false, true, false]);
    }

    [Fact]
    public async Task Save_AccountRepositoryError_MarksAccountAndClearsItsFilter()
    {
        await LoadAccountsAsync("a", "b");
        var account = _viewModel.Accounts[0];
        var repository = account.Repositories[0];
        _viewModel.SelectedAccount = _viewModel.Accounts[1];
        _viewModel.SelectedSection = SettingsSection.Tools;
        account.RepositoryView.Filter = "zzz";

        repository.Branch = "-x";
        var markedAtOnce = account.HasAnyErrors;
        await _viewModel.SaveCommand.ExecuteAsync(null);

        markedAtOnce.ShouldBeTrue();
        _viewModel.SelectedSection.ShouldBe(SettingsSection.Accounts);
        _viewModel.SelectedAccount.ShouldBe(account);
        account.RepositoryView.Filter.ShouldBeEmpty();
        account.RepositoryView.Items.ShouldContain(repository);
    }

    [Fact]
    public async Task Save_StandaloneRepositoryError_SelectsRepositoriesAndClearsFilter()
    {
        await LoadAsync();
        _viewModel.RepositoryView.Filter = "zzz";
        _viewModel.Repositories[0].Url = "https://github.com/other";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.SelectedSection.ShouldBe(SettingsSection.Repositories);
        _viewModel.RepositoriesSection.HasErrors.ShouldBeTrue();
        _viewModel.AccountsSection.HasErrors.ShouldBeFalse();
        _viewModel.RepositoryView.Filter.ShouldBeEmpty();
        _viewModel.RepositoryView.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Save_ErrorsInSeveralSections_SelectsFirstInNavigationOrder()
    {
        _telemetryStore.Validate(Arg.Any<TelemetrySettings>()).Returns(["OpenTelemetry:OtlpEndpoint must be an absolute URI."]);
        await LoadAsync();
        _viewModel.SelectedSection = SettingsSection.Token;
        _viewModel.OtlpEndpoint = "localhost";
        _viewModel.Token = "   ";
        _viewModel.Repositories[0].Url = "https://github.com/other";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.SelectedSection.ShouldBe(SettingsSection.Repositories);
        _viewModel.Sections.Where(section => section.HasErrors).Select(section => section.Section)
            .ShouldBe([SettingsSection.Repositories, SettingsSection.Telemetry, SettingsSection.Token]);
    }

    [Fact]
    public async Task Save_WhitespaceToken_MarksTokenUntilUserTypes()
    {
        await LoadAsync();
        _viewModel.Token = "   ";

        await _viewModel.SaveCommand.ExecuteAsync(null);
        var marked = _viewModel.TokenSection.HasErrors;
        var selected = _viewModel.SelectedSection;
        _viewModel.Token = _typedToken;

        marked.ShouldBeTrue();
        selected.ShouldBe(SettingsSection.Token);
        _viewModel.TokenSection.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public async Task Save_InvalidTelemetry_MarksAndSelectsTelemetry()
    {
        _telemetryStore.Validate(Arg.Any<TelemetrySettings>()).Returns(["OpenTelemetry:Headers must be name=value pairs."]);
        await LoadAsync();
        _viewModel.TelemetryHeaders = "bad";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.TelemetrySection.HasErrors.ShouldBeTrue();
        _viewModel.SelectedSection.ShouldBe(SettingsSection.Telemetry);
    }

    [Fact]
    public async Task Save_SchemaErrorOnly_KeepsSectionAndMarksNone()
    {
        var config = CreateConfig();
        config.SchemaVersion = 2;
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(config, []));
        await LoadAsync();
        _viewModel.SelectedSection = SettingsSection.Tools;
        _viewModel.BackupRoot = @"D:\NewBackups";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.HasGeneralErrors.ShouldBeTrue();
        _viewModel.SelectedSection.ShouldBe(SettingsSection.Tools);
        _viewModel.Sections.ShouldAllBe(section => !section.HasErrors);
    }

    [Fact]
    public async Task RemoveAccount_WithError_ClearsAccountsMark()
    {
        await LoadAccountsAsync("a", "b");
        _viewModel.Accounts[1].Path = "relative";
        var marked = _viewModel.AccountsSection.HasErrors;

        _viewModel.RemoveAccountCommand.Execute(_viewModel.Accounts[1]);

        marked.ShouldBeTrue();
        _viewModel.AccountsSection.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveAccountRepository_WithError_ClearsAccountMark()
    {
        await LoadAsync();
        var account = _viewModel.Accounts[0];
        account.Repositories[0].Branch = "-x";

        account.RemoveRepositoryCommand.Execute(account.Repositories[0]);

        account.HasAnyErrors.ShouldBeFalse();
        _viewModel.AccountsSection.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public async Task AddRepository_FilterActive_ClearsFilterAndShowsNewRowLast()
    {
        await LoadAsync();
        _viewModel.RepositoryView.Filter = "zzz";

        _viewModel.AddRepositoryCommand.Execute(null);

        _viewModel.RepositoryView.Filter.ShouldBeEmpty();
        _viewModel.RepositoryView.Items.Count.ShouldBe(2);
        _viewModel.RepositoryView.Items[^1].ShouldBe(_viewModel.Repositories[^1]);
        _viewModel.RepositoryView.HasNoMatch.ShouldBeFalse();
    }

    [Fact]
    public async Task RepositoryView_FilterByUrl_KeepsFileOrderInSave()
    {
        await LoadAsync();
        _viewModel.AddRepositoryCommand.Execute(null);
        _viewModel.Repositories[1].Url = "https://github.com/octo/zeta";

        _viewModel.RepositoryView.Filter = "ZETA";
        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.RepositoryView.Items.ShouldHaveSingleItem().ShouldBe(_viewModel.Repositories[1]);
        SavedConfig().Repositories.Select(repository => repository.Url)
            .ShouldBe(["https://github.com/other/lib", "https://github.com/octo/zeta"]);
    }

    [Fact]
    public async Task ConfigNotReadable_NavigationWorksAndToolsErrorSelectsTools()
    {
        _toolPathsStore.Validate(Arg.Any<ToolPaths>()).Returns(["Tools:GitPath must be an absolute path when set."]);
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(
            new BackupConfigException(_configPath, ["backupRoot must be set."]));
        await LoadAsync();
        _viewModel.SelectedSection = SettingsSection.Telemetry;
        var telemetrySelected = _viewModel.TelemetrySection.IsSelected;
        _viewModel.SelectedSection = SettingsSection.General;
        _viewModel.GitPath = "git.exe";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        telemetrySelected.ShouldBeTrue();
        _viewModel.IsLoaded.ShouldBeFalse();
        _viewModel.SelectedAccount.ShouldBeNull();
        _viewModel.SelectedSection.ShouldBe(SettingsSection.Tools);
        _viewModel.ToolsSection.HasErrors.ShouldBeTrue();
    }

    /// <summary>
    /// Creates the configuration the store returns: one account with two repositories and one standalone repository.
    /// </summary>
    /// <returns>A new, valid configuration.</returns>
    private static BackupConfig CreateConfig() => new()
    {
        BackupRoot = @"D:\Backups",
        SourcesRoot = @"E:\Sources",
        Exclude = ["someone/*", "*-archive"],
        Accounts =
        [
            new()
            {
                Url = "https://github.com/octo",
                Exclude = ["test-*"],
                Repositories =
                [
                    new() { Name = "tool", Id = 101 },
                    new() { Name = "old", Id = 102, Branch = "legacy", Path = @"F:\Old", Status = RepositoryStatus.Unavailable },
                ],
            },
        ],
        Repositories = [new() { Url = "https://github.com/other/lib", Id = 201 }],
    };

    /// <summary>
    /// Sets <see cref="BackupConfig.DiscoverAccessibleRepositories"/> of <paramref name="config"/>.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="value">The value.</param>
    /// <returns>The same configuration.</returns>
    private static BackupConfig WithDiscover(BackupConfig config, bool value)
    {
        config.DiscoverAccessibleRepositories = value;
        return config;
    }

    /// <summary>
    /// Returns the error messages shown at a field.
    /// </summary>
    /// <param name="form">The form that holds the field.</param>
    /// <param name="propertyName">The property bound to the field.</param>
    /// <returns>The messages.</returns>
    private static string?[] ErrorsOf(SettingsFormViewModel form, string propertyName) =>
        [.. form.GetErrors(propertyName).Select(result => result.ErrorMessage)];

    /// <summary>
    /// Reads a configuration with one account per login, each with one repository, and no standalone repositories.
    /// </summary>
    /// <param name="logins">The logins of the accounts, in file order.</param>
    /// <returns>A task that completes when the editor is filled.</returns>
    private Task LoadAccountsAsync(params string[] logins)
    {
        var config = new BackupConfig
        {
            BackupRoot = @"D:\Backups",
            Accounts =
            [
                .. logins.Select((login, index) => new AccountConfig
                {
                    Url = $"https://github.com/{login}",
                    Repositories = [new() { Name = "tool", Id = 300 + index }],
                }),
            ],
        };
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(config, []));
        return LoadAsync();
    }

    /// <summary>
    /// Reads the configuration into the editor.
    /// </summary>
    /// <returns>A task that completes when the editor is filled.</returns>
    private Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    /// <summary>
    /// Returns the configuration of the only save.
    /// </summary>
    /// <returns>The saved configuration.</returns>
    private BackupConfig SavedConfig() =>
        (BackupConfig)_configStore.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IBackupConfigStore.SaveAsync))
            .GetArguments()[0]!;

    /// <summary>
    /// Asserts that no log entry carries the token, in its message or in a structured value.
    /// </summary>
    private void AssertTokenNotLogged() =>
        _logger.Collector.GetSnapshot().ShouldAllBe(record =>
            !record.Message.Contains(_token)
            && (record.StructuredState == null || record.StructuredState.All(pair => pair.Value == null || !pair.Value.Contains(_token))));
}
