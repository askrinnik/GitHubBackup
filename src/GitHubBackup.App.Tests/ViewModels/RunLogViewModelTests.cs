using System.Globalization;
using GitHubBackup.App.Tests.Logging;
using GitHubBackup.App.Tests.Runs;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="RunLogViewModel"/>: the subscription to the feed, batching the entries into the UI thread, the limit
/// of kept entries and clearing.
/// </summary>
public sealed class RunLogViewModelTests : IDisposable
{
    private static readonly DateTimeOffset _timestamp = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualUiDispatcher _dispatcher = new();

    private readonly FakeLiveLogFeed _feed = new();

    private readonly RunLogViewModel _viewModel;

    /// <summary>
    /// Creates the ViewModel over a dispatcher the test runs by hand and a feed the test writes to.
    /// </summary>
    public RunLogViewModelTests() => _viewModel = new(_dispatcher, _feed);

    [Fact]
    public void Constructor_SubscribesToFeed()
    {
        _feed.Write(Entry(1));
        _dispatcher.RunAll();

        _feed.SubscriberCount.ShouldBe(1);
        _viewModel.Entries.Select(entry => entry.Message).ShouldBe(["1"]);
    }

    [Fact]
    public void Dispose_UnsubscribesOnce()
    {
        var subscription = Substitute.For<IDisposable>();
        var feed = Substitute.For<ILiveLogFeed>();
        feed.Subscribe(Arg.Any<Action<LiveLogEntry>>()).Returns(subscription);
        var viewModel = new RunLogViewModel(_dispatcher, feed);

        viewModel.Dispose();
        viewModel.Dispose();

        subscription.Received(1).Dispose();
    }

    [Fact]
    public void ClearCommand_ShownAndWaiting_RemovesBoth()
    {
        _feed.Write(Entry(1));
        _dispatcher.RunAll();
        _feed.Write(Entry(2));

        _viewModel.ClearCommand.Execute(null);
        _dispatcher.RunAll();

        _viewModel.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Clear_FlushPostedThenNewEntry_ShowsOnlyNewEntry()
    {
        _feed.Write(Entry(1));
        _dispatcher.PendingCount.ShouldBe(1);

        _viewModel.Clear();
        _feed.Write(Entry(2));
        _dispatcher.RunAll();

        _viewModel.Entries.Select(entry => entry.Message).ShouldBe(["2"]);
    }

    [Fact]
    public void ClearCommand_CanExecute_AlwaysTrue() => _viewModel.ClearCommand.CanExecute(null).ShouldBeTrue();

    [Fact]
    public void Add_ExactlyLimit_KeepsAll()
    {
        for (var i = 1; i <= RunLogViewModel.MaxEntries; i++)
        {
            _viewModel.Add(Entry(i));
        }

        _dispatcher.RunAll();

        _viewModel.Entries.Count.ShouldBe(RunLogViewModel.MaxEntries);
        _viewModel.Entries[0].Message.ShouldBe("1");
    }

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

    /// <summary>
    /// Unsubscribes the ViewModel from the feed.
    /// </summary>
    public void Dispose() => _viewModel.Dispose();
}
