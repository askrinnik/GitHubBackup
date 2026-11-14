using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Shell;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="ArchiveHistoryViewModel"/>: the repositories, the archives of the selected repository, the marking
/// of missing archives and the Explorer commands.
/// </summary>
public sealed class ArchiveHistoryViewModelTests
{
    private const int _repositoriesLoadFailedEventId = 10;

    private const int _archiveHistoryLoadFailedEventId = 11;

    private const int _explorerFailedEventId = 12;

    private const string _token = "ghp_ArchiveTokenValue0123456789abcdefABC";

    private const string _toolFolder = @"C:\backup\octo\tool";

    private static readonly DateTimeOffset _createdAt = new(2026, 11, 5, 9, 7, 30, TimeSpan.FromHours(2));

    private static readonly RepositoryOverviewEntry _tool = CreateEntry("octo", "tool", "octo", 1, _toolFolder);

    private static readonly RepositoryOverviewEntry _lib = CreateEntry("other", "lib", null, 42, @"C:\backup\other\lib");

    private readonly IRepositoryOverviewProvider _overviewProvider = Substitute.For<IRepositoryOverviewProvider>();

    private readonly IArchiveHistoryProvider _historyProvider = Substitute.For<IArchiveHistoryProvider>();

    private readonly IExplorerLauncher _explorer = Substitute.For<IExplorerLauncher>();

    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private readonly SecretMasker _masker = new();

    private readonly FakeLogger<ArchiveHistoryViewModel> _logger = new();

    private readonly ArchiveHistoryViewModel _viewModel;

    /// <summary>
    /// Creates the ViewModel over an overview with one account repository and one standalone repository, whose
    /// archive histories are empty with existing folders.
    /// </summary>
    public ArchiveHistoryViewModelTests()
    {
        _overviewProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(new RepositoryOverview(
            [new("octo", "https://github.com/octo", [_tool])],
            [_lib]));
        _historyProvider.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ArchiveHistory(call.ArgAt<string>(0), true, []));
        _viewModel = new(_overviewProvider, _historyProvider, _explorer, _dialogs, _masker, _logger);
    }

    [Fact]
    public async Task LoadRepositories_Overview_ListsAccountRepositoriesThenStandalone()
    {
        await _viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        _viewModel.Repositories.ShouldBe([_tool, _lib]);
        _viewModel.SelectedRepository.ShouldBeNull();
        _viewModel.RepositoriesError.ShouldBeNull();
    }

    [Fact]
    public async Task LoadRepositories_ConfigFails_ShowsMaskedErrorAndEmptyList()
    {
        _masker.Register(_token);
        _overviewProvider.LoadAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new BackupConfigException(@"C:\app\backup-config.json", [$"bad {_token}"]));

        await _viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        _viewModel.Repositories.ShouldBeEmpty();
        _viewModel.HasRepositoriesError.ShouldBeTrue();
        _viewModel.RepositoriesError.ShouldNotBeNull().ShouldNotContain(_token);
        _viewModel.RepositoriesError.ShouldContain("***");
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_repositoriesLoadFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task LoadRepositories_SelectedRepositoryStillListed_KeepsSelection()
    {
        await _viewModel.LoadRepositoriesCommand.ExecuteAsync(null);
        _viewModel.SelectedRepository = _lib;

        await _viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        _viewModel.SelectedRepository.ShouldBe(_lib);
    }

    [Fact]
    public async Task SelectRepository_Archives_ShowsRowsAndFolder()
    {
        UseHistory(true, CreateEntry("tool.zip", fileExists: true));
        await _viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

        _viewModel.SelectedRepository = _tool;

        await _historyProvider.Received(1).LoadAsync(_toolFolder, 1, "octo/tool", Arg.Any<CancellationToken>());
        _viewModel.ArchiveFolder.ShouldBe(_toolFolder);
        _viewModel.FolderExists.ShouldBeTrue();
        _viewModel.Archives.ShouldHaveSingleItem().File.ShouldBe("tool.zip");
        _viewModel.IsEmpty.ShouldBeFalse();
        _viewModel.LoadError.ShouldBeNull();
    }

    [Fact]
    public void SelectRepository_NoArchives_IsEmpty()
    {
        _viewModel.SelectedRepository = _tool;

        _viewModel.Archives.ShouldBeEmpty();
        _viewModel.IsEmpty.ShouldBeTrue();
        ArchiveHistoryViewModel.NoArchivesMessage.ShouldBe("No archives");
    }

    [Fact]
    public void MissingArchive_RowIsMissingTrue()
    {
        UseHistory(true, CreateEntry("present.zip", fileExists: true), CreateEntry("gone.zip", fileExists: false));

        _viewModel.SelectedRepository = _tool;

        _viewModel.Archives.Select(row => (row.File, row.IsMissing, row.FileState))
            .ShouldBe([("present.zip", false, "Present"), ("gone.zip", true, "Missing")]);
    }

    [Fact]
    public void SelectRepository_StateFileInvalid_ShowsMaskedErrorAndLogs()
    {
        _masker.Register(_token);
        _historyProvider.LoadAsync(_toolFolder, 1, "octo/tool", Arg.Any<CancellationToken>()).ThrowsAsync(
            new BackupStateException(@"C:\backup\octo\tool\.backup-state.json", [$"The id does not match {_token}."]));

        _viewModel.SelectedRepository = _tool;

        _viewModel.Archives.ShouldBeEmpty();
        _viewModel.IsEmpty.ShouldBeFalse();
        _viewModel.HasLoadError.ShouldBeTrue();
        _viewModel.LoadError.ShouldBe(@"The backup state 'C:\backup\octo\tool\.backup-state.json' is invalid: The id does not match ***.");
        _viewModel.OpenFolderCommand.CanExecute(null).ShouldBeFalse();
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_archiveHistoryLoadFailedEventId);
        record.GetStructuredStateValue("Repository").ShouldBe("octo/tool");
    }

    [Fact]
    public void SelectRepository_OtherRepositoryAfterError_ClearsError()
    {
        _historyProvider.LoadAsync(_toolFolder, Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BackupStateException(@"C:\backup\octo\tool\.backup-state.json", ["broken"]));
        _viewModel.SelectedRepository = _tool;

        _viewModel.SelectedRepository = _lib;

        _viewModel.LoadError.ShouldBeNull();
        _viewModel.ArchiveFolder.ShouldBe(@"C:\backup\other\lib");
    }

    [Fact]
    public void SelectRepository_Cleared_ClearsArchivesAndFolder()
    {
        UseHistory(true, CreateEntry("tool.zip", fileExists: true));
        _viewModel.SelectedRepository = _tool;

        _viewModel.SelectedRepository = null;

        _viewModel.Archives.ShouldBeEmpty();
        _viewModel.ArchiveFolder.ShouldBeNull();
        _viewModel.IsEmpty.ShouldBeFalse();
        _viewModel.OpenFolderCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task SelectRepository_ChangedWhileReading_ReadsNewSelection()
    {
        var pending = new TaskCompletionSource<ArchiveHistory>();
        _historyProvider.LoadAsync(_toolFolder, Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        _viewModel.SelectedRepository = _tool;
        var reading = _viewModel.LoadArchivesCommand.ExecutionTask.ShouldNotBeNull();

        _viewModel.SelectedRepository = _lib;
        pending.SetResult(new(_toolFolder, true, [CreateEntry("tool.zip", fileExists: true)]));
        await reading;

        _viewModel.ArchiveFolder.ShouldBe(@"C:\backup\other\lib");
        _viewModel.Archives.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void OpenFolder_FolderExists_CanExecute(bool folderExists, bool expected)
    {
        UseHistory(folderExists);

        _viewModel.SelectedRepository = _tool;

        _viewModel.OpenFolderCommand.CanExecute(null).ShouldBe(expected);
    }

    [Fact]
    public void OpenFolder_NoRepository_CannotExecute() =>
        _viewModel.OpenFolderCommand.CanExecute(null).ShouldBeFalse();

    [Fact]
    public async Task OpenFolder_Executed_OpensArchiveFolder()
    {
        _viewModel.SelectedRepository = _tool;

        await _viewModel.OpenFolderCommand.ExecuteAsync(null);

        await _explorer.Received(1).OpenFolderAsync(_toolFolder, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenFolder_ExplorerFails_ShowsMaskedDialogAndLogs()
    {
        _masker.Register(_token);
        _explorer.OpenFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProcessStartException($"explorer.exe cannot be started {_token}"));
        _viewModel.SelectedRepository = _tool;

        await _viewModel.OpenFolderCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowError(ArchiveHistoryViewModel.ExplorerFailedTitle, "explorer.exe cannot be started ***");
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_explorerFailedEventId);
        record.Level.ShouldBe(LogLevel.Error);
        record.GetStructuredStateValue("Path").ShouldBe(_toolFolder);
    }

    [Fact]
    public void ShowInExplorer_NoArchiveSelected_CannotExecute()
    {
        UseHistory(true, CreateEntry("tool.zip", fileExists: true));

        _viewModel.SelectedRepository = _tool;

        _viewModel.ShowInExplorerCommand.CanExecute(null).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShowInExplorer_SelectedArchive_CanExecuteWhenFileExists(bool fileExists, bool expected)
    {
        UseHistory(true, CreateEntry("tool.zip", fileExists));
        _viewModel.SelectedRepository = _tool;
        var raised = 0;
        _viewModel.ShowInExplorerCommand.CanExecuteChanged += (_, _) => raised++;

        _viewModel.SelectedArchive = _viewModel.Archives[0];

        _viewModel.ShowInExplorerCommand.CanExecute(null).ShouldBe(expected);
        raised.ShouldBe(1);
    }

    [Fact]
    public async Task ShowInExplorer_Executed_SelectsArchiveFile()
    {
        UseHistory(true, CreateEntry("tool.zip", fileExists: true));
        _viewModel.SelectedRepository = _tool;
        _viewModel.SelectedArchive = _viewModel.Archives[0];

        await _viewModel.ShowInExplorerCommand.ExecuteAsync(null);

        await _explorer.Received(1).ShowFileAsync(@"C:\backup\octo\tool\tool.zip", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowInExplorer_ExplorerFails_ShowsDialog()
    {
        _explorer.ShowFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProcessStartException("explorer.exe cannot be started"));
        UseHistory(true, CreateEntry("tool.zip", fileExists: true));
        _viewModel.SelectedRepository = _tool;
        _viewModel.SelectedArchive = _viewModel.Archives[0];

        await _viewModel.ShowInExplorerCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowError(ArchiveHistoryViewModel.ExplorerFailedTitle, "explorer.exe cannot be started");
    }

    [Fact]
    public void CreateRow_Entry_FormatsEveryCell()
    {
        var entry = new ArchiveHistoryEntry(
            BackupTarget.Wiki,
            "tool.wiki.zip",
            @"C:\backup\octo\tool\tool.wiki.zip",
            _createdAt,
            "master",
            "0123456789abcdef0123",
            1536,
            ["heads/master", "tags/v1"],
            true);

        var row = ArchiveHistoryRow.Create(entry);

        row.ShouldBe(new ArchiveHistoryRow
        {
            Target = "Wiki",
            File = "tool.wiki.zip",
            FullPath = @"C:\backup\octo\tool\tool.wiki.zip",
            CreatedAt = "2026-11-05 09:07:30",
            Branch = "master",
            HeadCommit = "0123456789abcdef0123",
            ShortHash = "0123456789",
            Size = "1.5 KB",
            ChangedRefs = "heads/master, tags/v1",
            IsMissing = false,
        });
    }

    [Fact]
    public void CreateRow_ShortCommit_KeepsWholeCommit() =>
        ArchiveHistoryRow.Create(CreateEntry("tool.zip", fileExists: true)).ShortHash.ShouldBe("a1b2c3");

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(10_485_760L, "10.0 MB")]
    [InlineData(3_221_225_472L, "3.0 GB")]
    [InlineData(2_199_023_255_552L, "2.0 TB")]
    public void FormatSize_Bytes_UsesLargestUnit(long bytes, string expected) =>
        ArchiveHistoryRow.FormatSize(bytes).ShouldBe(expected);

    /// <summary>
    /// Makes the archive history of every repository hold <paramref name="archives"/>.
    /// </summary>
    /// <param name="folderExists">Whether the archive folder exists.</param>
    /// <param name="archives">The archives.</param>
    private void UseHistory(bool folderExists, params ArchiveHistoryEntry[] archives) =>
        _historyProvider.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ArchiveHistory(call.ArgAt<string>(0), folderExists, archives));

    /// <summary>
    /// Creates an archive of the repository <c>octo/tool</c>.
    /// </summary>
    /// <param name="file">The file name of the archive.</param>
    /// <param name="fileExists">Whether the file is in the archive folder.</param>
    /// <returns>The archive.</returns>
    private static ArchiveHistoryEntry CreateEntry(string file, bool fileExists) => new(
        BackupTarget.Repository, file, Path.Combine(_toolFolder, file), _createdAt, "main", "a1b2c3", 10, [], fileExists);

    /// <summary>
    /// Creates a repository of the overview.
    /// </summary>
    /// <param name="owner">The owner.</param>
    /// <param name="name">The name.</param>
    /// <param name="account">The account that lists the repository, or <see langword="null"/>.</param>
    /// <param name="id">The GitHub id.</param>
    /// <param name="archiveFolder">The archive folder.</param>
    /// <returns>The repository.</returns>
    private static RepositoryOverviewEntry CreateEntry(string owner, string name, string? account, long id, string archiveFolder) =>
        new(owner, name, account, RepositoryOverviewStatus.Active, null, null) { RepositoryId = id, ArchiveFolder = archiveFolder };
}
