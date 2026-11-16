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
/// Tests <see cref="ArchiveHistoryViewModel"/>: the repository table, the archives of the selected repository, the
/// marking of missing archives and files without a record, and the Explorer commands.
/// </summary>
public sealed class ArchiveHistoryViewModelTests
{
    private const int _repositoriesLoadFailedEventId = 10;

    private const int _archiveHistoryLoadFailedEventId = 11;

    private const int _explorerFailedEventId = 12;

    private const string _token = "ghp_ArchiveTokenValue0123456789abcdefABC";

    private const string _toolFolder = @"C:\backup\octo\tool";

    private const string _libFolder = @"C:\backup\other\lib";

    private static readonly DateTimeOffset _createdAt = new(2026, 11, 5, 9, 7, 30, TimeSpan.FromHours(2));

    private static readonly RepositoryOverviewEntry _tool = CreateRepository("octo", "tool", "octo", 1, _toolFolder);

    private static readonly RepositoryOverviewEntry _lib = CreateRepository("other", "lib", null, 42, _libFolder);

    private readonly IArchiveSummaryProvider _summaryProvider = Substitute.For<IArchiveSummaryProvider>();

    private readonly IExplorerLauncher _explorer = Substitute.For<IExplorerLauncher>();

    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private readonly SecretMasker _masker = new();

    private readonly FakeLogger<ArchiveHistoryViewModel> _logger = new();

    private readonly ArchiveHistoryViewModel _viewModel;

    /// <summary>
    /// Creates the ViewModel over one account repository and one standalone repository, whose archive histories are
    /// empty with existing folders.
    /// </summary>
    public ArchiveHistoryViewModelTests()
    {
        UseSummaries(Summary(_tool, true), Summary(_lib, true));
        _viewModel = new(_summaryProvider, _explorer, _dialogs, _masker, _logger);
    }

    [Fact]
    public async Task LoadRepositories_Summaries_ListsRowsInConfigurationOrder()
    {
        await LoadAsync();

        _viewModel.Repositories.Select(row => (row.Repository, row.Account, row.RepositoryId, row.ArchiveFolder))
            .ShouldBe([("octo/tool", "octo", 1L, _toolFolder), ("other/lib", "", 42L, _libFolder)]);
        _viewModel.SelectedRepository.ShouldBeNull();
        _viewModel.RepositoriesError.ShouldBeNull();
    }

    [Fact]
    public async Task LoadRepositories_ConfigFails_ShowsMaskedErrorAndEmptyTable()
    {
        _masker.Register(_token);
        _summaryProvider.LoadAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new BackupConfigException(@"C:\app\backup-config.json", [$"bad {_token}"]));

        await LoadAsync();

        _viewModel.Repositories.ShouldBeEmpty();
        _viewModel.HasRepositoriesError.ShouldBeTrue();
        _viewModel.RepositoriesError.ShouldNotBeNull().ShouldNotContain(_token);
        _viewModel.RepositoriesError.ShouldContain("***");
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_repositoriesLoadFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task LoadRepositories_SelectedRepositoryStillListed_KeepsSelectionWithNewArchives()
    {
        await LoadAsync();
        _viewModel.SelectedRepository = _viewModel.Repositories[1];
        UseSummaries(Summary(_tool, true), Summary(_lib, true, CreateEntry("lib.zip", fileExists: true)));

        await LoadAsync();

        _viewModel.SelectedRepository.ShouldBeSameAs(_viewModel.Repositories[1]);
        _viewModel.Archives.ShouldHaveSingleItem().File.ShouldBe("lib.zip");
    }

    [Fact]
    public async Task LoadRepositories_SelectedRepositoryGone_ClearsSelectionAndArchives()
    {
        UseSummaries(Summary(_tool, true, CreateEntry("tool.zip", fileExists: true)), Summary(_lib, true));
        await LoadAsync();
        _viewModel.SelectedRepository = _viewModel.Repositories[0];
        UseSummaries(Summary(_lib, true));

        await LoadAsync();

        _viewModel.SelectedRepository.ShouldBeNull();
        _viewModel.Archives.ShouldBeEmpty();
        _viewModel.ArchiveFolder.ShouldBeNull();
    }

    [Fact]
    public async Task LoadRepositories_History_RowShowsCountersSizeAndLastArchive()
    {
        UseSummaries(Summary(
            _tool,
            true,
            CreateEntry("present.zip", fileExists: true, diskSize: 1024),
            CreateEntry("gone.zip", fileExists: false),
            CreateEntry("extra.zip", fileExists: true, isInState: false, diskSize: 512)));

        await LoadAsync();

        var row = _viewModel.Repositories.ShouldHaveSingleItem();
        (row.InState, row.InStateText, row.OnDisk, row.Missing, row.NotInState, row.SizeBytes, row.Size)
            .ShouldBe((2, "2", 2, 1, 1, 1536L, "1.5 KB"));
        row.LastArchiveAt.ShouldBe(_createdAt);
        row.LastArchive.ShouldBe("2026-11-05 09:07:30");
        row.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadRepositories_StateFileInvalid_RowShowsMaskedErrorAndLogs()
    {
        _masker.Register(_token);
        var error = new BackupStateException(
            @"C:\backup\octo\tool\.backup-state.json", [$"The id does not match {_token}."]);
        UseSummaries(new RepositoryArchiveSummary(_tool, null, error), Summary(_lib, true));

        await LoadAsync();

        var row = _viewModel.Repositories[0];
        row.HasError.ShouldBeTrue();
        row.Error.ShouldBe(@"The backup state 'C:\backup\octo\tool\.backup-state.json' is invalid: The id does not match ***.");
        row.InStateText.ShouldBe(ArchiveRepositoryRow.ErrorText);
        (row.InState, row.OnDisk, row.Missing, row.NotInState, row.SizeBytes).ShouldBe((null, null, null, null, null));
        (row.Size, row.LastArchive).ShouldBe(("", ""));
        _viewModel.Repositories[1].HasError.ShouldBeFalse();
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_archiveHistoryLoadFailedEventId);
        record.GetStructuredStateValue("Repository").ShouldBe("octo/tool");
    }

    [Fact]
    public async Task SelectRepository_Archives_ShowsRowsAndFolder()
    {
        UseSummaries(Summary(_tool, true, CreateEntry("tool.zip", fileExists: true)));
        await LoadAsync();

        _viewModel.SelectedRepository = _viewModel.Repositories[0];

        _viewModel.ArchiveFolder.ShouldBe(_toolFolder);
        _viewModel.FolderExists.ShouldBeTrue();
        _viewModel.Archives.ShouldHaveSingleItem().File.ShouldBe("tool.zip");
        _viewModel.IsEmpty.ShouldBeFalse();
        _viewModel.LoadError.ShouldBeNull();
    }

    [Fact]
    public async Task SelectRepository_NoArchives_IsEmpty()
    {
        await LoadAsync();

        _viewModel.SelectedRepository = _viewModel.Repositories[0];

        _viewModel.Archives.ShouldBeEmpty();
        _viewModel.IsEmpty.ShouldBeTrue();
        ArchiveHistoryViewModel.NoArchivesMessage.ShouldBe("No archives");
    }

    [Fact]
    public async Task SelectRepository_MissingAndUnrecordedArchives_RowsShowFileState()
    {
        UseSummaries(Summary(
            _tool,
            true,
            CreateEntry("present.zip", fileExists: true),
            CreateEntry("gone.zip", fileExists: false),
            CreateEntry("extra.zip", fileExists: true, isInState: false)));
        await LoadAsync();

        _viewModel.SelectedRepository = _viewModel.Repositories[0];

        _viewModel.Archives.Select(row => (row.File, row.IsMissing, row.IsNotInState, row.FileState)).ShouldBe(
        [
            ("present.zip", false, false, "Present"),
            ("gone.zip", true, false, "Missing"),
            ("extra.zip", false, true, "Not in state"),
        ]);
    }

    [Fact]
    public async Task SelectRepository_RowWithError_ShowsErrorAboveArchives()
    {
        UseSummaries(new RepositoryArchiveSummary(_tool, null, new IOException("The folder cannot be listed.")));
        await LoadAsync();

        _viewModel.SelectedRepository = _viewModel.Repositories[0];

        _viewModel.Archives.ShouldBeEmpty();
        _viewModel.IsEmpty.ShouldBeFalse();
        _viewModel.HasLoadError.ShouldBeTrue();
        _viewModel.LoadError.ShouldBe("The folder cannot be listed.");
        _viewModel.ArchiveFolder.ShouldBe(_toolFolder);
        _viewModel.OpenFolderCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task SelectRepository_OtherRepositoryAfterError_ClearsError()
    {
        UseSummaries(new RepositoryArchiveSummary(_tool, null, new IOException("broken")), Summary(_lib, true));
        await LoadAsync();
        _viewModel.SelectedRepository = _viewModel.Repositories[0];

        _viewModel.SelectedRepository = _viewModel.Repositories[1];

        _viewModel.LoadError.ShouldBeNull();
        _viewModel.ArchiveFolder.ShouldBe(_libFolder);
    }

    [Fact]
    public async Task SelectRepository_Cleared_ClearsArchivesAndFolder()
    {
        UseSummaries(Summary(_tool, true, CreateEntry("tool.zip", fileExists: true)));
        await LoadAsync();
        _viewModel.SelectedRepository = _viewModel.Repositories[0];

        _viewModel.SelectedRepository = null;

        _viewModel.Archives.ShouldBeEmpty();
        _viewModel.ArchiveFolder.ShouldBeNull();
        _viewModel.IsEmpty.ShouldBeFalse();
        _viewModel.OpenFolderCommand.CanExecute(null).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task OpenFolder_FolderExists_CanExecute(bool folderExists, bool expected)
    {
        UseSummaries(Summary(_tool, folderExists));
        await LoadAsync();

        _viewModel.SelectedRepository = _viewModel.Repositories[0];

        _viewModel.OpenFolderCommand.CanExecute(null).ShouldBe(expected);
    }

    [Fact]
    public void OpenFolder_NoRepository_CannotExecute() =>
        _viewModel.OpenFolderCommand.CanExecute(null).ShouldBeFalse();

    [Fact]
    public async Task OpenFolder_Executed_OpensArchiveFolder()
    {
        await SelectToolAsync();

        await _viewModel.OpenFolderCommand.ExecuteAsync(null);

        await _explorer.Received(1).OpenFolderAsync(_toolFolder, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenFolder_ExplorerFails_ShowsMaskedDialogAndLogs()
    {
        _masker.Register(_token);
        _explorer.OpenFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProcessStartException($"explorer.exe cannot be started {_token}"));
        await SelectToolAsync();

        await _viewModel.OpenFolderCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowError(ArchiveHistoryViewModel.ExplorerFailedTitle, "explorer.exe cannot be started ***");
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_explorerFailedEventId);
        record.Level.ShouldBe(LogLevel.Error);
        record.GetStructuredStateValue("Path").ShouldBe(_toolFolder);
    }

    [Fact]
    public async Task ShowInExplorer_NoArchiveSelected_CannotExecute()
    {
        await SelectToolAsync(CreateEntry("tool.zip", fileExists: true));

        _viewModel.ShowInExplorerCommand.CanExecute(null).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ShowInExplorer_SelectedArchive_CanExecuteWhenFileExists(bool fileExists, bool expected)
    {
        await SelectToolAsync(CreateEntry("tool.zip", fileExists));
        var raised = 0;
        _viewModel.ShowInExplorerCommand.CanExecuteChanged += (_, _) => raised++;

        _viewModel.SelectedArchive = _viewModel.Archives[0];

        _viewModel.ShowInExplorerCommand.CanExecute(null).ShouldBe(expected);
        raised.ShouldBe(1);
    }

    [Fact]
    public async Task ShowInExplorer_FileWithoutRecord_CanExecute()
    {
        await SelectToolAsync(CreateEntry("extra.zip", fileExists: true, isInState: false));

        _viewModel.SelectedArchive = _viewModel.Archives[0];

        _viewModel.ShowInExplorerCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ShowInExplorer_Executed_SelectsArchiveFile()
    {
        await SelectToolAsync(CreateEntry("tool.zip", fileExists: true));
        _viewModel.SelectedArchive = _viewModel.Archives[0];

        await _viewModel.ShowInExplorerCommand.ExecuteAsync(null);

        await _explorer.Received(1).ShowFileAsync(@"C:\backup\octo\tool\tool.zip", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowInExplorer_ExplorerFails_ShowsDialog()
    {
        _explorer.ShowFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProcessStartException("explorer.exe cannot be started"));
        await SelectToolAsync(CreateEntry("tool.zip", fileExists: true));
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
            true,
            true,
            1536);

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
            IsNotInState = false,
        });
    }

    [Fact]
    public void CreateRow_FileWithoutRecord_EmptyBranchAndNotInState()
    {
        var row = ArchiveHistoryRow.Create(new ArchiveHistoryEntry(
            BackupTarget.Repository,
            "octo_tool_20261105-090730_0123456789.zip",
            @"C:\backup\octo\tool\octo_tool_20261105-090730_0123456789.zip",
            _createdAt,
            null,
            "0123456789",
            2048,
            [],
            true,
            false,
            2048));

        (row.Branch, row.ShortHash, row.Size, row.ChangedRefs, row.IsMissing, row.IsNotInState, row.FileState)
            .ShouldBe(("", "0123456789", "2.0 KB", "", false, true, ArchiveHistoryRow.NotInStateText));
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
    /// Reads the repository table.
    /// </summary>
    /// <returns>A task that completes when the table is read.</returns>
    private Task LoadAsync() => _viewModel.LoadRepositoriesCommand.ExecuteAsync(null);

    /// <summary>
    /// Makes <c>octo/tool</c> the only repository, with <paramref name="archives"/> in an existing folder, reads the
    /// table and selects the repository.
    /// </summary>
    /// <param name="archives">The archives of the repository.</param>
    /// <returns>A task that completes when the repository is selected.</returns>
    private async Task SelectToolAsync(params ArchiveHistoryEntry[] archives)
    {
        UseSummaries(Summary(_tool, true, archives));
        await LoadAsync();
        _viewModel.SelectedRepository = _viewModel.Repositories[0];
    }

    /// <summary>
    /// Makes the summary provider return <paramref name="summaries"/>.
    /// </summary>
    /// <param name="summaries">The summaries in configuration order.</param>
    private void UseSummaries(params RepositoryArchiveSummary[] summaries) =>
        _summaryProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(summaries);

    /// <summary>
    /// Creates the summary of a repository whose history holds <paramref name="archives"/>.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="folderExists">Whether the archive folder exists.</param>
    /// <param name="archives">The archives.</param>
    /// <returns>The summary.</returns>
    private static RepositoryArchiveSummary Summary(
        RepositoryOverviewEntry repository, bool folderExists, params ArchiveHistoryEntry[] archives) =>
        new(repository, new(repository.ArchiveFolder, folderExists, archives), null);

    /// <summary>
    /// Creates an archive of the repository <c>octo/tool</c>.
    /// </summary>
    /// <param name="file">The file name of the archive.</param>
    /// <param name="fileExists">Whether the file is in the archive folder.</param>
    /// <param name="isInState">Whether the state file records the archive.</param>
    /// <param name="diskSize">The size of the file on the disk when it exists.</param>
    /// <returns>The archive.</returns>
    private static ArchiveHistoryEntry CreateEntry(
        string file, bool fileExists, bool isInState = true, long diskSize = 10) => new(
        BackupTarget.Repository,
        file,
        Path.Combine(_toolFolder, file),
        _createdAt,
        isInState ? "main" : null,
        "a1b2c3",
        10,
        [],
        fileExists,
        isInState,
        fileExists ? diskSize : null);

    /// <summary>
    /// Creates a repository of the overview.
    /// </summary>
    /// <param name="owner">The owner.</param>
    /// <param name="name">The name.</param>
    /// <param name="account">The account that lists the repository, or <see langword="null"/>.</param>
    /// <param name="id">The GitHub id.</param>
    /// <param name="archiveFolder">The archive folder.</param>
    /// <returns>The repository.</returns>
    private static RepositoryOverviewEntry CreateRepository(
        string owner, string name, string? account, long id, string archiveFolder) =>
        new(owner, name, account, RepositoryOverviewStatus.Active, null, null)
        {
            RepositoryId = id,
            ArchiveFolder = archiveFolder,
        };
}
