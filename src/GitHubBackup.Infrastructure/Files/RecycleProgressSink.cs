using System.Runtime.InteropServices.Marshalling;

namespace GitHubBackup.Infrastructure.Files;

/// <summary>
/// Watches the deletion of one item: cancels it unless the Shell moves the item to the Recycle Bin, and records its
/// result.
/// </summary>
[GeneratedComClass]
internal sealed partial class RecycleProgressSink : IFileOperationProgressSink
{
    /// <summary>The HRESULT that succeeds.</summary>
    private const int _ok = 0;

    /// <summary>The HRESULT of <c>ERROR_CANCELLED</c>, which cancels the deletion.</summary>
    private const int _cancelled = unchecked((int)0x800704C7);

    /// <summary>Gets whether the sink cancelled a deletion that would not have gone to the Recycle Bin.</summary>
    public bool RefusedPermanentDeletion { get; private set; }

    /// <summary>Gets the result of the deletion, or <see langword="null"/> when the Shell did not report one.</summary>
    public int? DeleteResult { get; private set; }

    /// <summary>Gets whether the Shell reported an item in the Recycle Bin for the deleted item.</summary>
    public bool MovedToRecycleBin { get; private set; }

    /// <inheritdoc />
    public int StartOperations() => _ok;

    /// <inheritdoc />
    public int FinishOperations(int result) => _ok;

    /// <inheritdoc />
    public int PreRenameItem(uint flags, nint item, nint newName) => _ok;

    /// <inheritdoc />
    public int PostRenameItem(uint flags, nint item, nint newName, int result, nint newItem) => _ok;

    /// <inheritdoc />
    public int PreMoveItem(uint flags, nint item, nint destination, nint newName) => _ok;

    /// <inheritdoc />
    public int PostMoveItem(uint flags, nint item, nint destination, nint newName, int result, nint newItem) => _ok;

    /// <inheritdoc />
    public int PreCopyItem(uint flags, nint item, nint destination, nint newName) => _ok;

    /// <inheritdoc />
    public int PostCopyItem(uint flags, nint item, nint destination, nint newName, int result, nint newItem) => _ok;

    /// <inheritdoc />
    /// <remarks>
    /// The Shell clears the recycle flag when the Recycle Bin cannot take the item and would delete it permanently
    /// instead; the deletion is then cancelled.
    /// </remarks>
    public int PreDeleteItem(uint flags, nint item)
    {
        if (ShellRecycleBin.ShouldAllowDelete(flags))
        {
            return _ok;
        }

        RefusedPermanentDeletion = true;
        return _cancelled;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The Shell passes the item in the Recycle Bin as <paramref name="newItem"/>, and <c>null</c> when it deleted the
    /// item without keeping it.
    /// </remarks>
    public int PostDeleteItem(uint flags, nint item, int result, nint newItem)
    {
        DeleteResult = result;
        MovedToRecycleBin = newItem != 0;
        return _ok;
    }

    /// <inheritdoc />
    public int PreNewItem(uint flags, nint destination, nint newName) => _ok;

    /// <inheritdoc />
    public int PostNewItem(
        uint flags, nint destination, nint newName, nint templateName, uint attributes, int result, nint newItem) => _ok;

    /// <inheritdoc />
    public int UpdateProgress(uint workTotal, uint workSoFar) => _ok;

    /// <inheritdoc />
    public int ResetTimer() => _ok;

    /// <inheritdoc />
    public int PauseTimer() => _ok;

    /// <inheritdoc />
    public int ResumeTimer() => _ok;
}
