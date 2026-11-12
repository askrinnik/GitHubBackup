using GitHubBackup.Infrastructure.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Infrastructure.IntegrationTests.Runs;

/// <summary>
/// Tests <see cref="NamedMutexRunLock"/> with real mutexes of the operating system under a name unique to each test.
/// </summary>
public sealed class NamedMutexRunLockTests
{
    private const int _abandonedEventId = 1;

    private readonly string _name = $@"Local\GitHubBackup.Tests.{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TryAcquireAsync_Free_ReturnsHandle()
    {
        using var handle = await CreateLock().TryAcquireAsync(CancellationToken);

        handle.ShouldNotBeNull();
    }

    [Fact]
    public async Task TryAcquireAsync_HeldByAnotherInstance_ReturnsNull()
    {
        using var first = await CreateLock().TryAcquireAsync(CancellationToken);

        var second = await CreateLock().TryAcquireAsync(CancellationToken);

        first.ShouldNotBeNull();
        second.ShouldBeNull();
    }

    [Fact]
    public async Task TryAcquireAsync_AfterRelease_ReturnsHandle()
    {
        var first = await CreateLock().TryAcquireAsync(CancellationToken);
        first.ShouldNotBeNull().Dispose();

        using var second = await CreateLock().TryAcquireAsync(CancellationToken);

        second.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dispose_FromAnotherThread_ReleasesLock()
    {
        var first = (await CreateLock().TryAcquireAsync(CancellationToken)).ShouldNotBeNull();

        await Task.Run(first.Dispose, CancellationToken);

        using var second = await CreateLock().TryAcquireAsync(CancellationToken);
        second.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dispose_Twice_DoesNotThrow()
    {
        var handle = (await CreateLock().TryAcquireAsync(CancellationToken)).ShouldNotBeNull();
        handle.Dispose();

        Should.NotThrow(handle.Dispose);
    }

    [Fact]
    public async Task TryAcquireAsync_Abandoned_ReturnsHandleAndLogsWarning()
    {
        using var abandoned = new Mutex(false, _name);
        var owner = new Thread(() => abandoned.WaitOne());
        owner.Start();
        owner.Join();
        var logger = new FakeLogger<NamedMutexRunLock>();

        using var handle = await new NamedMutexRunLock(_name, logger).TryAcquireAsync(CancellationToken);

        handle.ShouldNotBeNull();
        var record = logger.Collector.GetSnapshot().Where(record => record.Id.Id == _abandonedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Name").ShouldBe(_name);
    }

    [Fact]
    public async Task TryAcquireAsync_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => CreateLock().TryAcquireAsync(cancellation.Token));
    }

    [Fact]
    public void DefaultName_IsGlobal() => NamedMutexRunLock.DefaultName.ShouldBe(@"Global\GitHubBackup.BackupRun");

    /// <summary>
    /// Creates a lock over the mutex of this test.
    /// </summary>
    /// <returns>The lock.</returns>
    private NamedMutexRunLock CreateLock() => new(_name, NullLogger<NamedMutexRunLock>.Instance);
}
