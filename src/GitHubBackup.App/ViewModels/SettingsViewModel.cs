using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Hosting;
using GitHubBackup.App.Navigation;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Core.Tools;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the settings editor: edits <c>backup-config.json</c>, the tool paths, the telemetry settings and the token
/// saved in the token store with one Save, checks a token against the GitHub API and switches the theme.
/// </summary>
/// <remarks>
/// <para>
/// Every changed group is checked before anything is written, by the validators the stores apply on every read and
/// write; errors appear at their fields and nothing is written while there are any. The warnings of the validator
/// are shown while the user edits and never block saving.
/// </para>
/// <para>
/// The editor writes back every value it does not show, such as the id and status of a repository entry, as it was
/// read. The tool paths and a saved token take effect when the application starts again.
/// </para>
/// </remarks>
public sealed partial class SettingsViewModel : SettingsFormViewModel, ICloseGuard
{
    /// <summary>The title of the dialog that shows a failure to write a settings file.</summary>
    public const string SaveFailedTitle = "Settings not saved";

    /// <summary>The title of the dialog that asks before unsaved changes are discarded.</summary>
    public const string DiscardTitle = "Unsaved changes";

    /// <summary>The question of the dialog that asks before unsaved changes are discarded.</summary>
    public const string DiscardMessage = "The settings have unsaved changes. Discard them?";

    /// <summary>The error shown at the token field when it is empty; the same text as the console command.</summary>
    public const string BlankTokenMessage = "The token cannot be empty.";

    /// <summary>The title of the dialog that tells the saved token is shadowed by another source.</summary>
    public const string ShadowedTokenTitle = "Token saved";

    /// <summary>The source shown when the check uses the token typed in the field.</summary>
    public const string TypedTokenSource = "the token field";

    /// <summary>The error shown when GitHub rejects the checked token.</summary>
    public const string TokenRejectedMessage = "GitHub rejected the token: it is invalid or expired (HTTP 401).";

    /// <summary>The message shown when the user cancels the token check.</summary>
    public const string TokenCheckCancelledMessage = "The token check was cancelled.";

    /// <summary>The line shown when the server sends no scopes for a token.</summary>
    public const string ScopesNotReportedLine = "Scopes: not reported (a fine-grained token has permissions instead of scopes)";

    /// <summary>The line shown for anonymous access.</summary>
    public const string AnonymousLine = "No token: anonymous access, only public repositories are visible";

    private readonly IBackupConfigStore _configStore;
    private readonly IToolPathsStore _toolPathsStore;
    private readonly ITelemetrySettingsStore _telemetrySettingsStore;
    private readonly ITokenStore _tokenStore;
    private readonly ITokenProvider _tokenProvider;
    private readonly IGitHubTokenInspector _tokenInspector;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly ISecretMasker _secretMasker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly UiPreferences _preferences;

    /// <summary>The configuration as read, whose values the editor does not show are written back.</summary>
    private BackupConfig? _loaded;

    /// <summary>Whether the fields are being filled from the file, so their changes are not edits.</summary>
    private bool _populating;

    /// <summary>Whether the tool paths changed since they were read or saved.</summary>
    private bool _toolsDirty;

    /// <summary>Whether the telemetry settings changed since they were read or saved.</summary>
    private bool _telemetryDirty;

    /// <summary>The absolute path of the root folder of the archives.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateRequiredPath))]
    private string _backupRoot = string.Empty;

    /// <summary>The absolute path of the root folder of the clones; empty selects the folder next to the executable.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateOptionalPath))]
    private string _sourcesRoot = string.Empty;

    /// <summary>The exclusion masks applied to every repository, separated by commas.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateText))]
    private string _exclude = string.Empty;

    /// <summary>Whether repositories of owners that are not tracked accounts are offered by discovery.</summary>
    [ObservableProperty]
    private bool _discoverAccessibleRepositories;

    /// <summary>The absolute path of <c>git.exe</c>; empty takes git from <c>PATH</c>.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateOptionalPath))]
    private string _gitPath;

    /// <summary>The absolute path of <c>7z.exe</c>; empty selects the default location.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateOptionalPath))]
    private string _sevenZipPath;

    /// <summary>Whether traces and metrics are exported over OTLP.</summary>
    [ObservableProperty]
    private bool _telemetryEnabled;

    /// <summary>The OTLP endpoint (gRPC) that receives the telemetry; may be empty while telemetry is off.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateText))]
    private string _otlpEndpoint;

    /// <summary>The token typed by the user; cleared after it is saved.</summary>
    [ObservableProperty]
    private string _token = string.Empty;

    /// <summary>The error shown at the token field, or <see langword="null"/>.</summary>
    [ObservableProperty]
    private string? _tokenError;

    /// <summary>The lines that describe the outcome of the last successful token check.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _tokenCheckLines = [];

    /// <summary>Why the last token check failed, or <see langword="null"/>.</summary>
    [ObservableProperty]
    private string? _tokenCheckError;

    /// <summary>
    /// The theme chosen in the list; it applies and is saved at once, apart from the configuration, so Save and Cancel
    /// do not concern it.
    /// </summary>
    [ObservableProperty]
    private ThemeOption _selectedTheme;

    /// <summary>The errors that belong to no field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGeneralErrors))]
    private IReadOnlyList<string> _generalErrors = [];

    /// <summary>The warnings of the validator for the configuration in the fields.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    private IReadOnlyList<string> _warnings = [];

    /// <summary>Why <c>backup-config.json</c> cannot be read, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _loadError;

    /// <summary>
    /// The note that <c>backup-config.json</c> does not exist yet and that Save creates it, or <see langword="null"/>
    /// when the file exists.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewConfig))]
    private string? _newConfigMessage;

    /// <summary>Whether the configuration is read, so it can be edited and saved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLoaded;

    /// <summary>Whether the configuration in the fields differs from the file.</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// Initializes the editor with the tool paths and telemetry settings of the running application; the
    /// configuration is read by <see cref="LoadCommand"/>.
    /// </summary>
    /// <param name="configStore">Reads, validates and writes <c>backup-config.json</c>.</param>
    /// <param name="toolPathsStore">Reads, validates and writes the tool paths.</param>
    /// <param name="telemetrySettingsStore">Reads, validates and writes the telemetry settings.</param>
    /// <param name="tokenStore">Keeps the token the user saves.</param>
    /// <param name="tokenProvider">Finds the token the application uses, for the check and the shadowing warning.</param>
    /// <param name="tokenInspector">Asks the GitHub API about a token.</param>
    /// <param name="navigation">Returns to the repository tree.</param>
    /// <param name="dialogs">Shows write failures and asks before unsaved changes are discarded.</param>
    /// <param name="secretMasker">Masks secrets in messages before the editor shows them.</param>
    /// <param name="timeProvider">Supplies the local time zone of the rate limit reset time.</param>
    /// <param name="logger">Logs the failures of reading and writing.</param>
    /// <param name="preferences">Applies and saves the theme chosen in the list.</param>
    public SettingsViewModel(
        IBackupConfigStore configStore,
        IToolPathsStore toolPathsStore,
        ITelemetrySettingsStore telemetrySettingsStore,
        ITokenStore tokenStore,
        ITokenProvider tokenProvider,
        IGitHubTokenInspector tokenInspector,
        INavigationService navigation,
        IDialogService dialogs,
        ISecretMasker secretMasker,
        TimeProvider timeProvider,
        ILogger<SettingsViewModel> logger,
        UiPreferences preferences)
    {
        _configStore = configStore;
        _toolPathsStore = toolPathsStore;
        _telemetrySettingsStore = telemetrySettingsStore;
        _tokenStore = tokenStore;
        _tokenProvider = tokenProvider;
        _tokenInspector = tokenInspector;
        _navigation = navigation;
        _dialogs = dialogs;
        _secretMasker = secretMasker;
        _timeProvider = timeProvider;
        _logger = logger;
        _preferences = preferences;
        _selectedTheme = ThemeOptions.First(option => option.Value == preferences.Theme);

        var tools = toolPathsStore.Current;
        _gitPath = tools.GitPath ?? string.Empty;
        _sevenZipPath = tools.SevenZipPath ?? string.Empty;

        var telemetry = telemetrySettingsStore.Current;
        _telemetryEnabled = telemetry.Enabled;
        _otlpEndpoint = telemetry.OtlpEndpoint ?? string.Empty;
    }

    /// <summary>Gets the tracked accounts, in file order.</summary>
    public ObservableCollection<AccountSettingsViewModel> Accounts { get; } = [];

    /// <summary>Gets the standalone repositories, in file order.</summary>
    public ObservableCollection<RepositorySettingsViewModel> Repositories { get; } = [];

    /// <summary>Gets the entries of the theme list.</summary>
    public static IReadOnlyList<ThemeOption> ThemeOptions => ThemeOption.All;

    /// <summary>Gets the change of theme last started from the list, which completes when the theme is saved.</summary>
    internal Task ThemeChange { get; private set; } = Task.CompletedTask;

    /// <summary>Gets the location of the saved token, for the label of the token field.</summary>
    public string TokenStoreLocation => _tokenStore.Location;

    /// <summary>Gets whether errors that belong to no field are shown.</summary>
    public bool HasGeneralErrors => GeneralErrors.Count > 0;

    /// <summary>Gets whether warnings are shown.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Gets whether the configuration cannot be read.</summary>
    public bool HasLoadError => LoadError is not null;

    /// <summary>Gets whether the editor holds a configuration that is not yet saved to any file.</summary>
    public bool IsNewConfig => NewConfigMessage is not null;

    /// <summary>
    /// Gets whether leaving the editor would lose a change: the configuration, the tool paths, the telemetry settings or a
    /// typed token.
    /// </summary>
    public bool HasUnsavedChanges => IsDirty || _toolsDirty || _telemetryDirty || !string.IsNullOrEmpty(Token);

    /// <inheritdoc />
    /// <remarks>Unsaved changes are discarded only after the user confirms.</remarks>
    public Task<bool> CanCloseAsync() => Task.FromResult(ConfirmLeave());

    /// <summary>
    /// Gets whether Save is available: the configuration is read, or it cannot be read and only the other groups are
    /// saved.
    /// </summary>
    private bool CanSave => IsLoaded || HasLoadError;

    /// <summary>
    /// Returns the note the editor shows above the fields while <c>backup-config.json</c> does not exist.
    /// </summary>
    /// <param name="configPath">The path of the missing configuration file.</param>
    /// <returns>The note.</returns>
    public static string FormatNewConfigMessage(string configPath) =>
        $"The file '{configPath}' does not exist yet. Save creates it.";

    /// <summary>
    /// Reads <c>backup-config.json</c> into the fields, once; a failure is shown instead of the fields, and a missing
    /// file fills the fields with an empty configuration that Save creates.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the fields are filled or the error is shown.</returns>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            var result = await _configStore.LoadAsync(cancellationToken);
            Populate(result.Config);
            LoadError = null;
        }
        catch (BackupConfigNotFoundException exception)
        {
            AppLog.BackupConfigMissing(_logger, exception.ConfigPath);
            Populate(new BackupConfig());
            NewConfigMessage = _secretMasker.Mask(FormatNewConfigMessage(exception.ConfigPath));
            LoadError = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.SettingsLoadFailed(_logger, exception);
            var message = exception is BackupConfigException config
                ? string.Join(Environment.NewLine, config.Errors)
                : exception.Message;
            LoadError = _secretMasker.Mask(message);
        }
    }

    /// <summary>
    /// Validates every changed group, writes the changed groups and returns to the repository tree.
    /// </summary>
    /// <remarks>
    /// The changed groups are the configuration, the tool paths, the telemetry settings and a typed token. All of them
    /// are validated first; errors are shown at their fields and nothing is written while there are any. The groups are
    /// then written in that order and each one that is written stops counting as changed. A failure to write stops the
    /// save, is shown in a dialog and keeps the editor open with the fields as they are, so a retry writes only the
    /// groups that are still unsaved.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>A task that completes when the groups are written or the errors are shown.</returns>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        List<string> general = [];
        var valid = true;

        BackupConfig? config = null;
        if (IsLoaded && (IsDirty || IsNewConfig))
        {
            config = BuildConfig();
            var result = _configStore.Validate(config);
            Warnings = result.Warnings;
            general.AddRange(ShowConfigErrors(result.Errors));
            valid &= result.IsValid;
        }

        ToolPaths? tools = null;
        if (_toolsDirty)
        {
            tools = new ToolPaths(NullIfBlank(GitPath), NullIfBlank(SevenZipPath));
            valid &= ValidateTools(tools, general);
        }

        TelemetrySettings? telemetry = null;
        if (_telemetryDirty)
        {
            telemetry = new TelemetrySettings(TelemetryEnabled, NullIfBlank(OtlpEndpoint));
            valid &= ValidateTelemetry(telemetry, general);
        }

        string? token = null;
        if (!string.IsNullOrEmpty(Token))
        {
            if (string.IsNullOrWhiteSpace(Token))
            {
                TokenError = BlankTokenMessage;
                valid = false;
            }
            else
            {
                token = Token.Trim();
            }
        }

        GeneralErrors = [.. general.Select(_secretMasker.Mask)];
        if (!valid)
        {
            return;
        }

        try
        {
            if (config is not null)
            {
                await _configStore.SaveAsync(config, cancellationToken);
                _loaded = config;
                IsDirty = false;
                NewConfigMessage = null;
            }

            if (tools is not null)
            {
                await _toolPathsStore.SaveAsync(tools, cancellationToken);
                _toolsDirty = false;
            }

            if (telemetry is not null)
            {
                await _telemetrySettingsStore.SaveAsync(telemetry, cancellationToken);
                _telemetryDirty = false;
            }

            if (token is not null)
            {
                _secretMasker.Register(token);
                await _tokenStore.WriteAsync(token, cancellationToken);
                TokenError = null;
                Token = string.Empty;
            }
        }
        catch (BackupConfigException exception)
        {
            GeneralErrors = [.. exception.Errors.Select(_secretMasker.Mask)];
            return;
        }
        catch (TokenStoreException exception)
        {
            _dialogs.ShowError(SaveFailedTitle, _secretMasker.Mask(exception.Message));
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.SettingsSaveFailed(_logger, exception);
            _dialogs.ShowError(SaveFailedTitle, _secretMasker.Mask(exception.Message));
            return;
        }

        if (token is not null)
        {
            var effective = await _tokenProvider.GetTokenAsync(cancellationToken);
            if (effective.Value != token)
            {
                _dialogs.ShowInformation(
                    ShadowedTokenTitle,
                    $"The token from {effective.SourceName} takes priority over {_tokenStore.Location}, so the application does not use the saved token.");
            }
        }

        _navigation.NavigateTo<RepositoriesViewModel>();
    }

    /// <summary>
    /// Returns to the repository tree without writing anything, after the user confirms that unsaved changes are
    /// discarded.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (ConfirmLeave())
        {
            _navigation.NavigateTo<RepositoriesViewModel>();
        }
    }

    /// <summary>
    /// Adds an empty account.
    /// </summary>
    [RelayCommand]
    private void AddAccount()
    {
        Accounts.Add(new(new AccountConfig(), OnConfigEdited));
        OnConfigEdited();
    }

    /// <summary>
    /// Removes an account together with its repositories.
    /// </summary>
    /// <param name="account">The account to remove.</param>
    [RelayCommand]
    private void RemoveAccount(AccountSettingsViewModel? account)
    {
        if (account is not null && Accounts.Remove(account))
        {
            OnConfigEdited();
        }
    }

    /// <summary>
    /// Adds an empty standalone repository; its id is resolved by name on the next run.
    /// </summary>
    [RelayCommand]
    private void AddRepository()
    {
        Repositories.Add(new(new RepositoryConfig { Status = RepositoryStatus.Active }, OnConfigEdited));
        OnConfigEdited();
    }

    /// <summary>
    /// Removes a standalone repository.
    /// </summary>
    /// <param name="repository">The repository to remove.</param>
    [RelayCommand]
    private void RemoveRepository(RepositorySettingsViewModel? repository)
    {
        if (repository is not null && Repositories.Remove(repository))
        {
            OnConfigEdited();
        }
    }

    /// <summary>
    /// Asks the GitHub API about the typed token, or about the token the application uses when the field is empty.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that completes when the outcome is shown.</returns>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CheckTokenAsync(CancellationToken cancellationToken)
    {
        TokenCheckLines = [];
        TokenCheckError = null;
        try
        {
            string source;
            string? token;
            if (string.IsNullOrWhiteSpace(Token))
            {
                var effective = await _tokenProvider.GetTokenAsync(cancellationToken);
                source = effective.SourceName;
                token = effective.Value;
            }
            else
            {
                source = TypedTokenSource;
                token = Token.Trim();
                _secretMasker.Register(token);
            }

            var info = await _tokenInspector.InspectAsync(token, cancellationToken);
            TokenCheckLines = DescribeTokenInfo(source, info);
        }
        catch (OperationCanceledException)
        {
            TokenCheckError = TokenCheckCancelledMessage;
        }
        catch (GitHubAuthenticationException)
        {
            TokenCheckError = TokenRejectedMessage;
        }
        catch (GitHubApiException exception)
        {
            TokenCheckError = _secretMasker.Mask(exception.Message);
        }
        catch (Exception exception)
        {
            AppLog.TokenCheckFailed(_logger, exception);
            TokenCheckError = _secretMasker.Mask(exception.Message);
        }
    }

    /// <summary>
    /// Returns the lines that describe what the server reports about a token.
    /// </summary>
    /// <param name="source">Where the checked token came from.</param>
    /// <param name="info">What the server reports.</param>
    /// <returns>The lines.</returns>
    private List<string> DescribeTokenInfo(string source, GitHubTokenInfo info)
    {
        List<string> lines = [$"Token: {source}"];
        if (info.Login is { } login)
        {
            lines.Add($"Signed in as {login}");
            lines.Add(info.Scopes switch
            {
                null => ScopesNotReportedLine,
                [] => "Scopes: none",
                var scopes => $"Scopes: {string.Join(", ", scopes)}",
            });
        }
        else
        {
            lines.Add(AnonymousLine);
        }

        if (info.RateLimit is { } rateLimit)
        {
            var resetsAt = TimeZoneInfo.ConvertTime(rateLimit.ResetsAt, _timeProvider.LocalTimeZone);
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Rate limit: {rateLimit.Remaining} of {rateLimit.Limit} requests left, resets at {resetsAt:yyyy-MM-dd HH:mm}"));
        }
        else
        {
            lines.Add("Rate limit: not reported");
        }

        return lines;
    }

    /// <summary>
    /// Fills the fields from a configuration read from the file.
    /// </summary>
    /// <param name="config">The configuration.</param>
    private void Populate(BackupConfig config)
    {
        _populating = true;
        try
        {
            _loaded = config;
            BackupRoot = config.BackupRoot;
            SourcesRoot = config.SourcesRoot ?? string.Empty;
            Exclude = FormatMasks(config.Exclude);
            DiscoverAccessibleRepositories = config.DiscoverAccessibleRepositories;
            Accounts.Clear();
            foreach (var account in config.Accounts)
            {
                Accounts.Add(new(account, OnConfigEdited));
            }

            Repositories.Clear();
            foreach (var repository in config.Repositories)
            {
                Repositories.Add(new(repository, OnConfigEdited));
            }
        }
        finally
        {
            _populating = false;
        }

        IsLoaded = true;
        IsDirty = false;
        RefreshWarnings();
    }

    /// <summary>
    /// Creates the configuration in the fields, with the values the editor does not show taken from the file.
    /// </summary>
    /// <returns>A new configuration.</returns>
    private BackupConfig BuildConfig() => new()
    {
        SchemaVersion = _loaded?.SchemaVersion ?? BackupConfig.CurrentSchemaVersion,
        BackupRoot = BackupRoot.Trim(),
        SourcesRoot = NullIfBlank(SourcesRoot),
        Exclude = ParseMasks(Exclude),
        DiscoverAccessibleRepositories = DiscoverAccessibleRepositories,
        Accounts = [.. Accounts.Select(account => account.ToConfig())],
        Repositories = [.. Repositories.Select(repository => repository.ToConfig())],
    };

    /// <summary>
    /// Shows each validation error at its field and the rest as general errors, replacing the errors shown before.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    /// <returns>The errors that belong to no field.</returns>
    private List<string> ShowConfigErrors(IReadOnlyList<string> errors)
    {
        var fields = CollectFields();
        foreach (var form in fields.Values.Select(field => field.Form).Distinct())
        {
            if (form == this)
            {
                ClearReportedError(nameof(BackupRoot));
                ClearReportedError(nameof(SourcesRoot));
                ClearReportedError(nameof(Exclude));
            }
            else
            {
                form.ClearReportedErrors();
            }
        }

        List<string> general = [];
        foreach (var error in errors.Select(_secretMasker.Mask))
        {
            if (SettingsErrorMapper.FindLocation(error, fields.Keys) is { } location)
            {
                var field = fields[location];
                field.Form.ReportError(field.PropertyName, error);
            }
            else
            {
                general.Add(error);
            }
        }

        foreach (var form in fields.Values.Select(field => field.Form).Distinct())
        {
            if (form == this)
            {
                RefreshError(nameof(BackupRoot));
                RefreshError(nameof(SourcesRoot));
                RefreshError(nameof(Exclude));
            }
            else
            {
                form.RefreshErrors();
            }
        }

        return general;
    }

    /// <summary>
    /// Validates the tool paths and shows each error at its field.
    /// </summary>
    /// <param name="paths">The tool paths in the fields.</param>
    /// <param name="general">Receives the errors that belong to no field.</param>
    /// <returns><see langword="true"/> when the paths are valid.</returns>
    private bool ValidateTools(ToolPaths paths, List<string> general)
    {
        var errors = _toolPathsStore.Validate(paths);
        ClearReportedError(nameof(GitPath));
        ClearReportedError(nameof(SevenZipPath));
        foreach (var error in errors)
        {
            if (error.StartsWith(ToolPaths.GitPathKey, StringComparison.Ordinal))
            {
                ReportError(nameof(GitPath), error);
            }
            else if (error.StartsWith(ToolPaths.SevenZipPathKey, StringComparison.Ordinal))
            {
                ReportError(nameof(SevenZipPath), error);
            }
            else
            {
                general.Add(error);
            }
        }

        RefreshError(nameof(GitPath));
        RefreshError(nameof(SevenZipPath));
        return errors.Count == 0;
    }

    /// <summary>
    /// Validates the telemetry settings and shows each error at its field.
    /// </summary>
    /// <param name="settings">The telemetry settings in the fields.</param>
    /// <param name="general">Receives the errors that belong to no field.</param>
    /// <returns><see langword="true"/> when the settings are valid.</returns>
    private bool ValidateTelemetry(TelemetrySettings settings, List<string> general)
    {
        var errors = _telemetrySettingsStore.Validate(settings);
        ClearReportedError(nameof(OtlpEndpoint));
        foreach (var error in errors)
        {
            if (error.StartsWith(TelemetrySettings.OtlpEndpointKey, StringComparison.Ordinal))
            {
                ReportError(nameof(OtlpEndpoint), error);
            }
            else
            {
                general.Add(error);
            }
        }

        RefreshError(nameof(OtlpEndpoint));
        return errors.Count == 0;
    }

    /// <summary>
    /// Returns the field of every location of the configuration in the fields, by its location in the file.
    /// </summary>
    /// <returns>The fields by location.</returns>
    private Dictionary<string, SettingsField> CollectFields()
    {
        var fields = new Dictionary<string, SettingsField>(StringComparer.Ordinal)
        {
            ["backupRoot"] = new(this, nameof(BackupRoot)),
            ["sourcesRoot"] = new(this, nameof(SourcesRoot)),
            ["exclude"] = new(this, nameof(Exclude)),
        };

        for (var index = 0; index < Accounts.Count; index++)
        {
            Accounts[index].AddFields($"accounts[{index}]", fields);
        }

        for (var index = 0; index < Repositories.Count; index++)
        {
            Repositories[index].AddFields($"repositories[{index}]", fields);
        }

        return fields;
    }

    /// <summary>
    /// Validates the configuration in the fields and shows its warnings.
    /// </summary>
    private void RefreshWarnings()
    {
        if (IsLoaded)
        {
            Warnings = _configStore.Validate(BuildConfig()).Warnings;
        }
    }

    /// <summary>
    /// Returns whether the editor may be left, asking the user first when there are unsaved changes.
    /// </summary>
    /// <returns><see langword="true"/> when nothing would be lost or the user agrees to discard it.</returns>
    private bool ConfirmLeave() => !HasUnsavedChanges || _dialogs.Confirm(DiscardTitle, DiscardMessage);

    /// <summary>
    /// Marks the configuration as changed and shows the warnings of the new values.
    /// </summary>
    private void OnConfigEdited()
    {
        if (_populating)
        {
            return;
        }

        IsDirty = true;
        RefreshWarnings();
    }

    /// <summary>Marks the configuration as changed.</summary>
    /// <param name="value">The new root.</param>
    partial void OnBackupRootChanged(string value) => OnConfigEdited();

    /// <summary>Marks the configuration as changed.</summary>
    /// <param name="value">The new root.</param>
    partial void OnSourcesRootChanged(string value) => OnConfigEdited();

    /// <summary>Marks the configuration as changed.</summary>
    /// <param name="value">The new masks.</param>
    partial void OnExcludeChanged(string value) => OnConfigEdited();

    /// <summary>Marks the configuration as changed.</summary>
    /// <param name="value">The new value.</param>
    partial void OnDiscoverAccessibleRepositoriesChanged(bool value) => OnConfigEdited();

    /// <summary>Marks the tool paths as changed.</summary>
    /// <param name="value">The new path.</param>
    partial void OnGitPathChanged(string value) => _toolsDirty = true;

    /// <summary>Marks the tool paths as changed.</summary>
    /// <param name="value">The new path.</param>
    partial void OnSevenZipPathChanged(string value) => _toolsDirty = true;

    /// <summary>Marks the telemetry settings as changed.</summary>
    /// <param name="value">The new value.</param>
    partial void OnTelemetryEnabledChanged(bool value) => _telemetryDirty = true;

    /// <summary>Marks the telemetry settings as changed.</summary>
    /// <param name="value">The new endpoint.</param>
    partial void OnOtlpEndpointChanged(string value) => _telemetryDirty = true;

    /// <summary>Applies and saves the chosen theme.</summary>
    /// <param name="value">The chosen entry.</param>
    partial void OnSelectedThemeChanged(ThemeOption value) =>
        ThemeChange = _preferences.ChangeThemeAsync(value.Value, CancellationToken.None);

    /// <summary>Clears the error of the token field when the user types.</summary>
    /// <param name="value">The new token.</param>
    partial void OnTokenChanged(string value)
    {
        if (value.Length > 0)
        {
            TokenError = null;
        }
    }
}
