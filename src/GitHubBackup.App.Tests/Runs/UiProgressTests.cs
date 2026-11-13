using GitHubBackup.App.Runs;

namespace GitHubBackup.App.Tests.Runs;

/// <summary>
/// Tests <see cref="UiProgress{T}"/>.
/// </summary>
public sealed class UiProgressTests
{
    private readonly ManualUiDispatcher _dispatcher = new();

    private readonly List<int> _received = [];

    [Fact]
    public void Report_BeforeDispatcherRuns_DoesNotCallHandler()
    {
        var progress = new UiProgress<int>(_dispatcher, _received.Add);

        progress.Report(1);

        _received.ShouldBeEmpty();
        _dispatcher.PendingCount.ShouldBe(1);
    }

    [Fact]
    public void Report_DispatcherRuns_CallsHandlerWithEachValueInOrder()
    {
        var progress = new UiProgress<int>(_dispatcher, _received.Add);
        progress.Report(1);
        progress.Report(2);

        _dispatcher.RunAll();

        _received.ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Report_FromBackgroundThread_RunsHandlerWhereDispatcherRuns()
    {
        var handlerThreads = new List<int>();
        var progress = new UiProgress<int>(_dispatcher, _ => handlerThreads.Add(Environment.CurrentManagedThreadId));

        await Task.Run(() => progress.Report(1), TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        handlerThreads.ShouldBe([Environment.CurrentManagedThreadId]);
    }
}
