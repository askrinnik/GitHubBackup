using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using GitHubBackup.Core.Files;

namespace GitHubBackup.Infrastructure.Files;

/// <summary>
/// Moves files to the Recycle Bin with the Shell <see cref="IFileOperation"/>, refusing every deletion the Shell would
/// make permanent.
/// </summary>
/// <remarks>
/// <c>SHFileOperation</c> with <c>FOF_ALLOWUNDO</c> deletes a file permanently and silently when the Recycle Bin cannot
/// take it, so it is not used. Three guards keep a file from being lost: the volume must have a Recycle Bin, the
/// <see cref="RecycleProgressSink"/> cancels a deletion without the recycle flag, and success requires the Shell to
/// report the deletion as successful and the file to be gone.
/// </remarks>
/// <param name="fileSystem">Checks that the file exists before and is gone after the move.</param>
internal sealed partial class ShellRecycleBin(IFileSystem fileSystem) : IRecycleBin
{
    /// <summary>The <c>TSF_DELETE_RECYCLE_IF_POSSIBLE</c> flag the Shell passes when it moves an item to the Recycle Bin.</summary>
    internal const uint DeleteRecycleIfPossible = 0x80;

    /// <summary>
    /// The operation flags: <c>FOF_ALLOWUNDO</c>, <c>FOF_NO_UI</c>, <c>FOFX_RECYCLEONDELETE</c> and
    /// <c>FOFX_EARLYFAILURE</c>, so the deletion goes to the Recycle Bin without any dialog and stops at the first error.
    /// </summary>
    internal const uint OperationFlags = 0x40 | 0x614 | 0x0008_0000 | 0x0010_0000;

    /// <summary>The <c>CLSCTX_ALL</c> context of <see cref="CoCreateInstance"/>.</summary>
    private const uint _classContextAll = 0x17;

    /// <summary>The message of a file that the Recycle Bin refused.</summary>
    private const string _refusedMessage =
        "The Recycle Bin cannot take the file (it is turned off, too small for the file, or the volume has none), so the file is not deleted.";

    /// <summary>The class id of the Shell <c>FileOperation</c> object.</summary>
    private static readonly Guid _fileOperationClassId = new("3AD05575-8857-4850-9277-11B85BDB8E09");

    /// <summary>The interface id of <c>IShellItem</c>.</summary>
    private static readonly Guid _shellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    /// <summary>The wrappers that turn the native file operation into a managed object.</summary>
    private static readonly StrategyBasedComWrappers _comWrappers = new();

    /// <inheritdoc />
    /// <remarks>
    /// The Shell object needs a single-threaded apartment, so each call runs on a dedicated thread that ends with it.
    /// </remarks>
    public Task<RecycleOutcome> RecycleAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!fileSystem.File.Exists(path))
        {
            return Task.FromResult(RecycleOutcome.Failed($"The file '{path}' does not exist."));
        }

        var completion = new TaskCompletionSource<RecycleOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Recycle(path));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Recycle Bin",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>
    /// Returns whether a deletion with <paramref name="flags"/> goes to the Recycle Bin and may proceed.
    /// </summary>
    /// <param name="flags">The <c>TSF_</c> flags the Shell passes to <c>PreDeleteItem</c>.</param>
    /// <returns><see langword="true"/> when the Shell moves the item to the Recycle Bin.</returns>
    internal static bool ShouldAllowDelete(uint flags) => (flags & DeleteRecycleIfPossible) != 0;

    /// <summary>
    /// Moves the file to the Recycle Bin on the current single-threaded apartment thread.
    /// </summary>
    /// <param name="path">The absolute path of the existing file.</param>
    /// <returns>The outcome.</returns>
    private RecycleOutcome Recycle(string path)
    {
        var root = Path.GetPathRoot(path);
        var info = new RecycleBinInfo { Size = Unsafe.SizeOf<RecycleBinInfo>() };
        if (string.IsNullOrEmpty(root) || SHQueryRecycleBinW(root, ref info) < 0)
        {
            return RecycleOutcome.Failed($"The volume of '{path}' has no Recycle Bin, so the file is not deleted.");
        }

        var hr = CoCreateInstance(_fileOperationClassId, 0, _classContextAll, typeof(IFileOperation).GUID, out var pointer);
        if (hr < 0)
        {
            return RecycleOutcome.Failed($"The Shell file operation cannot be created: {Describe(hr)}");
        }

        var operation = (IFileOperation)_comWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
        Marshal.Release(pointer);
        try
        {
            return Recycle(operation, path);
        }
        catch (COMException exception)
        {
            return RecycleOutcome.Failed($"The Shell cannot move the file to the Recycle Bin: {Describe(exception.HResult)}");
        }
        finally
        {
            ((ComObject)(object)operation).FinalRelease();
        }
    }

    /// <summary>
    /// Queues and runs the deletion of one file with the recycle flags and decides the outcome.
    /// </summary>
    /// <param name="operation">The Shell file operation.</param>
    /// <param name="path">The absolute path of the existing file.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="COMException">A call of the file operation fails.</exception>
    private RecycleOutcome Recycle(IFileOperation operation, string path)
    {
        var sink = new RecycleProgressSink();
        operation.SetOperationFlags(OperationFlags);
        operation.Advise(sink, out var cookie);
        int performResult;
        bool aborted;
        try
        {
            var hr = SHCreateItemFromParsingName(path, 0, _shellItemId, out var item);
            if (hr < 0)
            {
                return RecycleOutcome.Failed($"The Shell cannot open the file: {Describe(hr)}");
            }

            try
            {
                operation.DeleteItem(item, 0);
            }
            finally
            {
                Marshal.Release(item);
            }

            performResult = operation.PerformOperations();
            operation.GetAnyOperationsAborted(out aborted);
        }
        finally
        {
            try
            {
                operation.Unadvise(cookie);
            }
            catch (COMException)
            {
                // The deletion is decided; failing to detach the sink does not change what happened to the file.
            }
        }

        if (sink.RefusedPermanentDeletion)
        {
            return RecycleOutcome.Failed(_refusedMessage);
        }

        if (sink.DeleteResult is { } deleteResult && deleteResult < 0)
        {
            return RecycleOutcome.Failed($"The file cannot be moved to the Recycle Bin: {Describe(deleteResult)}");
        }

        if (aborted || performResult < 0 || sink.DeleteResult is null)
        {
            return RecycleOutcome.Failed(performResult < 0
                ? $"The file cannot be moved to the Recycle Bin: {Describe(performResult)}"
                : "The move to the Recycle Bin was cancelled.");
        }

        if (!sink.MovedToRecycleBin)
        {
            return RecycleOutcome.Failed($"'{path}' was deleted but not moved to the Recycle Bin.");
        }

        return fileSystem.File.Exists(path)
            ? RecycleOutcome.Failed("The file is still in place after the move to the Recycle Bin.")
            : RecycleOutcome.Success;
    }

    /// <summary>
    /// Returns the system message of an HRESULT.
    /// </summary>
    /// <param name="hr">The failure HRESULT.</param>
    /// <returns>The message.</returns>
    private static string Describe(int hr) =>
        Marshal.GetExceptionForHR(hr)?.Message ?? $"error 0x{hr:X8}";

    /// <summary>Queries the size and item count of the Recycle Bin of a volume.</summary>
    /// <param name="rootPath">The root folder of the volume.</param>
    /// <param name="info">Receives the size and item count; its size field must be set.</param>
    /// <returns>The HRESULT; a failure when the volume has no Recycle Bin.</returns>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHQueryRecycleBinW(string rootPath, ref RecycleBinInfo info);

    /// <summary>Creates a Shell item for a file system path.</summary>
    /// <param name="path">The path.</param>
    /// <param name="bindContext">Zero.</param>
    /// <param name="interfaceId">The id of the requested interface.</param>
    /// <param name="item">Receives the interface pointer, which the caller releases.</param>
    /// <returns>The HRESULT.</returns>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid interfaceId, out nint item);

    /// <summary>Creates a COM object.</summary>
    /// <param name="classId">The class id.</param>
    /// <param name="outer">Zero.</param>
    /// <param name="context">The class context.</param>
    /// <param name="interfaceId">The id of the requested interface.</param>
    /// <param name="instance">Receives the interface pointer, which the caller releases.</param>
    /// <returns>The HRESULT.</returns>
    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(
        in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);

    /// <summary>
    /// The <c>SHQUERYRBINFO</c> structure with the natural alignment of a 64-bit process; in a 32-bit process its size
    /// does not match, the query fails and no file is moved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleBinInfo
    {
        /// <summary>The size of the structure in bytes.</summary>
        public int Size;

        /// <summary>The total size of the items in the Recycle Bin.</summary>
        public long ItemsSize;

        /// <summary>The number of items in the Recycle Bin.</summary>
        public long ItemCount;
    }
}
