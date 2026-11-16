using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace GitHubBackup.Infrastructure.Files;

/// <summary>
/// Declares the Shell <c>IFileOperation</c> interface, which copies, moves and deletes items as Explorer does.
/// </summary>
/// <remarks>
/// The members keep the order of the native vtable. Only the members <see cref="ShellRecycleBin"/> calls carry typed
/// parameters; the others hold their slot with pointer-sized placeholders.
/// </remarks>
[GeneratedComInterface]
[Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
internal partial interface IFileOperation
{
    /// <summary>Registers a sink that receives the notifications of every operation.</summary>
    /// <param name="sink">The sink.</param>
    /// <param name="cookie">Receives the value that <see cref="Unadvise"/> takes.</param>
    void Advise(IFileOperationProgressSink sink, out uint cookie);

    /// <summary>Removes a sink registered by <see cref="Advise"/>.</summary>
    /// <param name="cookie">The value <see cref="Advise"/> returned.</param>
    void Unadvise(uint cookie);

    /// <summary>Sets the <c>FOF_</c> and <c>FOFX_</c> flags of the operations.</summary>
    /// <param name="flags">The flags.</param>
    void SetOperationFlags(uint flags);

    /// <summary>Holds the slot of <c>SetProgressMessage</c>.</summary>
    /// <param name="message">Unused.</param>
    void SetProgressMessage(nint message);

    /// <summary>Holds the slot of <c>SetProgressDialog</c>.</summary>
    /// <param name="dialog">Unused.</param>
    void SetProgressDialog(nint dialog);

    /// <summary>Holds the slot of <c>SetProperties</c>.</summary>
    /// <param name="properties">Unused.</param>
    void SetProperties(nint properties);

    /// <summary>Holds the slot of <c>SetOwnerWindow</c>.</summary>
    /// <param name="owner">Unused.</param>
    void SetOwnerWindow(nint owner);

    /// <summary>Holds the slot of <c>ApplyPropertiesToItem</c>.</summary>
    /// <param name="item">Unused.</param>
    void ApplyPropertiesToItem(nint item);

    /// <summary>Holds the slot of <c>ApplyPropertiesToItems</c>.</summary>
    /// <param name="items">Unused.</param>
    void ApplyPropertiesToItems(nint items);

    /// <summary>Holds the slot of <c>RenameItem</c>.</summary>
    /// <param name="item">Unused.</param>
    /// <param name="newName">Unused.</param>
    /// <param name="sink">Unused.</param>
    void RenameItem(nint item, nint newName, nint sink);

    /// <summary>Holds the slot of <c>RenameItems</c>.</summary>
    /// <param name="items">Unused.</param>
    /// <param name="newName">Unused.</param>
    void RenameItems(nint items, nint newName);

    /// <summary>Holds the slot of <c>MoveItem</c>.</summary>
    /// <param name="item">Unused.</param>
    /// <param name="destination">Unused.</param>
    /// <param name="newName">Unused.</param>
    /// <param name="sink">Unused.</param>
    void MoveItem(nint item, nint destination, nint newName, nint sink);

    /// <summary>Holds the slot of <c>MoveItems</c>.</summary>
    /// <param name="items">Unused.</param>
    /// <param name="destination">Unused.</param>
    void MoveItems(nint items, nint destination);

    /// <summary>Holds the slot of <c>CopyItem</c>.</summary>
    /// <param name="item">Unused.</param>
    /// <param name="destination">Unused.</param>
    /// <param name="copyName">Unused.</param>
    /// <param name="sink">Unused.</param>
    void CopyItem(nint item, nint destination, nint copyName, nint sink);

    /// <summary>Holds the slot of <c>CopyItems</c>.</summary>
    /// <param name="items">Unused.</param>
    /// <param name="destination">Unused.</param>
    void CopyItems(nint items, nint destination);

    /// <summary>Queues the deletion of one item, which the flags turn into a move to the Recycle Bin.</summary>
    /// <param name="item">The <c>IShellItem</c> pointer of the item.</param>
    /// <param name="sink">An extra sink for this item, or zero.</param>
    void DeleteItem(nint item, nint sink);

    /// <summary>Holds the slot of <c>DeleteItems</c>.</summary>
    /// <param name="items">Unused.</param>
    void DeleteItems(nint items);

    /// <summary>Holds the slot of <c>NewItem</c>.</summary>
    /// <param name="destination">Unused.</param>
    /// <param name="attributes">Unused.</param>
    /// <param name="name">Unused.</param>
    /// <param name="templateName">Unused.</param>
    /// <param name="sink">Unused.</param>
    void NewItem(nint destination, uint attributes, nint name, nint templateName, nint sink);

    /// <summary>Runs the queued operations.</summary>
    /// <returns>The HRESULT; a failure when an operation failed or was cancelled.</returns>
    [PreserveSig]
    int PerformOperations();

    /// <summary>Tells whether an operation was cancelled or failed before it completed.</summary>
    /// <param name="anyAborted">Receives <see langword="true"/> when an operation was aborted.</param>
    void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool anyAborted);
}
