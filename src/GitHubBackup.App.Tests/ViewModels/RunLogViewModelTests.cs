using System.Globalization;
using GitHubBackup.App.Tests.Runs;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="RunLogViewModel"/>: batching the entries into the UI thread, the limit of kept entries and clearing.
/// </summary>
public sealed class RunLogViewModelTests
{
    private static readonly DateTimeOffset _timestamp = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualUiDispatcher _dispatcher = new();

    private readonly RunLogViewModel _viewModel;

    /// <summary>
    /// Creates the ViewModel over a dispatcher the test runs by hand.
    /// </summary>
    public RunLogViewModelTests() => _viewModel = new(_dispatcher);

    [Fact]
    public void Add_ManyEntriesBeforeUiThreadRuns_PostsOnceAndShowsAllInOrder()
    {
        _viewModel.Add(Entry(1));
        _viewModel.Add(Entry(2));
        _viewModel.Add(Entry(3));

        _dispatcher.PendingCount.ShouldBe(1);
        _viewModel.Entries.ShouldBeEmpty();
        _dispatcher.RunAll();
        _viewModel.Entries.Select(entry => entry.Message).ShouldBe(["1", "2", "3"]);
    }

    [Fact]
    public void Add_AfterBatchIsShown_PostsAgain()
    {
        _viewModel.Add(Entry(1));
        _dispatcher.RunAll();

        _viewModel.Add(Entry(2));

        _dispatcher.PendingCount.ShouldBe(1);
        _dispatcher.RunAll();
        _viewModel.Entries.Select(entry => entry.Message).ShouldBe(["1", "2"]);
    }

    [Fact]
    public void Add_MoreThanLimitAcrossBatches_KeepsNewestEntries()
    {
        for (var i = 1; i <= RunLogViewModel.MaxEntries; i++)
        {
            _viewModel.Add(Entry(i));
        }

        _dispatcher.RunAll();

        _viewModel.Add(Entry(RunLogViewModel.MaxEntries + 1));
        _viewModel.Add(Entry(RunLogViewModel.MaxEntries + 2));
        _dispatcher.RunAll();

        _viewModel.Entries.Count.ShouldBe(RunLogViewModel.MaxEntries);
        _viewModel.Entries[0].Message.ShouldBe("3");
        _viewModel.Entries[^1].Message.ShouldBe((RunLogViewModel.MaxEntries + 2).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Add_MoreThanLimitInOneBatch_KeepsNewestEntries()
    {
        for (var i = 1; i <= RunLogViewModel.MaxEntries + 10; i++)
        {
            _viewModel.Add(Entry(i));
        }

        _dispatcher.RunAll();

        _viewModel.Entries.Count.ShouldBe(RunLogViewModel.MaxEntries);
        _viewModel.Entries[0].Message.ShouldBe("11");
    }

    [Fact]
    public void Clear_EntriesShownAndWaiting_RemovesBoth()
    {
        _viewModel.Add(Entry(1));
        _dispatcher.RunAll();
        _viewModel.Add(Entry(2));

        _viewModel.Clear();
        _dispatcher.RunAll();

        _viewModel.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_FromManyThreads_ShowsEveryEntry()
    {
        const int threads = 4;
        const int perThread = 250;

        await Task.WhenAll(Enumerable.Range(0, threads).Select(thread => Task.Run(
            () =>
            {
                for (var i = 0; i < perThread; i++)
                {
                    _viewModel.Add(Entry((thread * perThread) + i));
                }
            },
            TestContext.Current.CancellationToken)));
        _dispatcher.RunAll();

        _viewModel.Entries.Select(entry => entry.Message).Distinct().Count().ShouldBe(threads * perThread);
    }

    /// <summary>
    /// Creates an entry whose message is <paramref name="number"/>.
    /// </summary>
    /// <param name="number">The number that identifies the entry.</param>
    /// <returns>The entry.</returns>
    private static LiveLogEntry Entry(int number) =>
        new(_timestamp, LogLevel.Information, number.ToString(CultureInfo.InvariantCulture));
}
