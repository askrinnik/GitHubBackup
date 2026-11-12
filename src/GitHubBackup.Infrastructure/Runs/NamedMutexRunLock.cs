using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Runs;

/// <summary>
/// Implements <see cref="IRunLock"/> with a named mutex of the operating system, shared by every session and user of
/// the machine.
/// </summary>
/// <remarks>
/// A mutex is owned by the thread that acquired it and only that thread may release it, while the run continues on
/// thread-pool threads after each <see langword="await"/>. Each acquisition therefore runs on a dedicated background
/// thread that takes the mutex, waits until the handle is disposed and releases it. When the process ends without
/// releasing, the operating system marks the mutex abandoned and the next run takes it over.
/// </remarks>
internal sealed class NamedMutexRunLock : IRunLock
{
    /// <summary>The name of the mutex; the <c>Global\</c> prefix makes it one mutex for all sessions.</summary>
    public const string DefaultName = @"Global\GitHubBackup.BackupRun";

    private readonly string _name;
    private readonly ILogger<NamedMutexRunLock> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedMutexRunLock"/> class over <see cref="DefaultName"/>.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public NamedMutexRunLock(ILogger<NamedMutexRunLock> logger)
        : this(DefaultName, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedMutexRunLock"/> class over a mutex of the given name.
    /// </summary>
    /// <param name="name">The name of the mutex.</param>
    /// <param name="logger">The logger.</param>
    internal NamedMutexRunLock(string name, ILogger<NamedMutexRunLock> logger)
    {
        _name = name;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A mutex whose access control list denies this user belongs to a run of another user, so it counts as held.
    /// </remarks>
    public Task<IDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var acquired = new TaskCompletionSource<IDisposable?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Hold(acquired))
        {
            IsBackground = true,
            Name = "GitHubBackup run lock",
        };
        thread.Start();
        return acquired.Task;
    }

    /// <summary>
    /// Takes the mutex on the current thread, reports the outcome and, when it is taken, keeps it until the handle is
    /// disposed.
    /// </summary>
    /// <param name="acquired">Receives the handle, or <see langword="null"/> when the mutex is held elsewhere.</param>
    private void Hold(TaskCompletionSource<IDisposable?> acquired)
    {
        try
        {
            using var mutex = new Mutex(false, _name);
            bool owned;
            try
            {
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                NamedMutexRunLockLog.Abandoned(_logger, _name);
                owned = true;
            }

            if (!owned)
            {
                acquired.SetResult(null);
                return;
            }

            using var released = new ManualResetEventSlim();
            using var handle = new Handle(released, Thread.CurrentThread);
            acquired.SetResult(handle);
            released.Wait();
            mutex.ReleaseMutex();
        }
        catch (UnauthorizedAccessException exception)
        {
            NamedMutexRunLockLog.AccessDenied(_logger, _name, exception);
            acquired.TrySetResult(null);
        }
        catch (Exception exception)
        {
            acquired.TrySetException(exception);
        }
    }

    /// <summary>
    /// Releases the mutex when disposed, from any thread, and returns once the owning thread has released it.
    /// </summary>
    /// <param name="released">Signals the owning thread to release the mutex.</param>
    /// <param name="owner">The thread that owns the mutex.</param>
    private sealed class Handle(ManualResetEventSlim released, Thread owner) : IDisposable
    {
        private int _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            released.Set();
            if (Thread.CurrentThread != owner)
            {
                owner.Join();
            }
        }
    }
}
