using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.State;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.History;

/// <summary>
/// Tests <see cref="ArchiveSummaryProvider"/> over a substituted overview provider and archive history provider.
/// </summary>
public sealed class ArchiveSummaryProviderTests
{
    private static readonly RepositoryOverviewEntry _tool = CreateEntry("octo", "tool", "octo", 1);

    private static readonly RepositoryOverviewEntry _app = CreateEntry("octo", "app", "octo", 2);

    private static readonly RepositoryOverviewEntry _lib = CreateEntry("other", "lib", null, 3);

    private readonly IRepositoryOverviewProvider _overviewProvider = Substitute.For<IRepositoryOverviewProvider>();

    private readonly IArchiveHistoryProvider _historyProvider = Substitute.For<IArchiveHistoryProvider>();

    private readonly ArchiveSummaryProvider _provider;

    /// <summary>
    /// Creates the provider over an overview with two account repositories and one standalone repository, whose
    /// histories are empty.
    /// </summary>
    public ArchiveSummaryProviderTests()
    {
        _overviewProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(new RepositoryOverview(
            [new("octo", "https://github.com/octo", [_tool, _app])],
            [_lib]));
        _historyProvider.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ArchiveHistory(call.ArgAt<string>(0), true, []));
        _provider = new(_overviewProvider, _historyProvider);
    }

    [Fact]
    public async Task LoadAsync_Overview_ReturnsRepositoriesInConfigurationOrder()
    {
        var summaries = await LoadAsync();

        summaries.Select(summary => summary.Repository).ShouldBe([_tool, _app, _lib]);
        summaries.ShouldAllBe(summary => summary.Error == null && summary.History != null);
    }

    [Fact]
    public async Task LoadAsync_Repository_ReadsHistoryOfItsArchiveFolder()
    {
        var history = new ArchiveHistory(_app.ArchiveFolder, false, []);
        _historyProvider.LoadAsync(_app.ArchiveFolder, 2, "octo/app", Arg.Any<CancellationToken>()).Returns(history);

        var summaries = await LoadAsync();

        summaries[1].History.ShouldBeSameAs(history);
    }

    [Theory]
    [InlineData(nameof(BackupStateException))]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(UnauthorizedAccessException))]
    public async Task LoadAsync_HistoryOfOneRepositoryFails_ErrorInItsRowOnly(string kind)
    {
        Exception error = kind switch
        {
            nameof(BackupStateException) => new BackupStateException(
                @"C:\backup\octo\tool\.backup-state.json", ["The file is not valid JSON."]),
            nameof(IOException) => new IOException("The folder cannot be listed."),
            _ => new UnauthorizedAccessException("Access is denied."),
        };
        _historyProvider.LoadAsync(_tool.ArchiveFolder, Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);

        var summaries = await LoadAsync();

        summaries[0].Error.ShouldBeSameAs(error);
        summaries[0].History.ShouldBeNull();
        summaries.Skip(1).ShouldAllBe(summary => summary.Error == null && summary.History != null);
    }

    [Fact]
    public async Task LoadAsync_ConfigFails_Propagates()
    {
        var error = new BackupConfigException(@"C:\app\backup-config.json", ["broken"]);
        _overviewProvider.LoadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(error);

        var exception = await Should.ThrowAsync<BackupConfigException>(LoadAsync);

        exception.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task LoadAsync_Cancelled_Propagates()
    {
        _historyProvider.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(LoadAsync);
    }

    /// <summary>
    /// Loads the summaries.
    /// </summary>
    /// <returns>The summaries.</returns>
    private Task<IReadOnlyList<RepositoryArchiveSummary>> LoadAsync() =>
        _provider.LoadAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Creates a repository of the overview whose archive folder is below <c>C:\backup</c>.
    /// </summary>
    /// <param name="owner">The owner.</param>
    /// <param name="name">The name.</param>
    /// <param name="account">The account that lists the repository, or <see langword="null"/>.</param>
    /// <param name="id">The GitHub id.</param>
    /// <returns>The repository.</returns>
    private static RepositoryOverviewEntry CreateEntry(string owner, string name, string? account, long id) =>
        new(owner, name, account, RepositoryOverviewStatus.Active, null, null)
        {
            RepositoryId = id,
            ArchiveFolder = $@"C:\backup\{owner}\{name}",
        };
}
