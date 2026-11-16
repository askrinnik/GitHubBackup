using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace GitHubBackup.Infrastructure.Files;

/// <summary>
/// Declares the Shell <c>IFileOperationProgressSink</c> interface, which <see cref="IFileOperation"/> calls before and
/// after each operation.
/// </summary>
/// <remarks>
/// The members keep the order of the native vtable and return their HRESULT; a failure from a <c>Pre</c> member cancels
/// that operation and the ones after it. Item and string parameters are raw pointers because the sink never uses them.
/// </remarks>
[GeneratedComInterface]
[Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2")]
internal partial interface IFileOperationProgressSink
{
    /// <summary>Called before the operations start.</summary>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int StartOperations();

    /// <summary>Called after the operations end.</summary>
    /// <param name="result">The result of the operations.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int FinishOperations(int result);

    /// <summary>Called before an item is renamed.</summary>
    /// <param name="flags">The <c>TSF_</c> flags.</param>
    /// <param name="item">The item.</param>
    /// <param name="newName">The new name.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PreRenameItem(uint flags, nint item, nint newName);

    /// <summary>Called after an item is renamed.</summary>
    /// <param name="flags">The <c>TSF_</c> flags.</param>
    /// <param name="item">The item.</param>
    /// <param name="newName">The new name.</param>
    /// <param name="result">The result of the rename.</param>
    /// <param name="newItem">The renamed item.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PostRenameItem(uint flags, nint item, nint newName, int result, nint newItem);

    /// <summary>Called before an item is moved.</summary>
    /// <param name="flags">The <c>TSF_</c> flags.</param>
    /// <param name="item">The item.</param>
    /// <param name="destination">The destination folder.</param>
    /// <param name="newName">The new name.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PreMoveItem(uint flags, nint item, nint destination, nint newName);

    /// <summary>Called after an item is moved.</summary>
    /// <param name="flags">The <c>TSF_</c> flags.</param>
    /// <param name="item">The item.</param>
    /// <param name="destination">The destination folder.</param>
    /// <param name="newName">The new name.</param>
    /// <param name="result">The result of the move.</param>
    /// <param name="newItem">The moved item.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PostMoveItem(uint flags, nint item, nint destination, nint newName, int result, nint newItem);

    /// <summary>Called before an item is copied.</summary>
    /// <param name="flags">The <c>TSF_</c> flags.</param>
    /// <param name="item">The item.</param>
    /// <param name="destination">The destination folder.</param>
    /// <param name="newName">The new name.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PreCopyItem(uint flags, nint item, nint destination, nint newName);

    /// <summary>Called after an item is copied.</summary>
    /// <param name="flags">The <c>TSF_</c> flags.</param>
    /// <param name="item">The item.</param>
    /// <param name="destination">The destination folder.</param>
    /// <param name="newName">The new name.</param>
    /// <param name="result">The result of the copy.</param>
    /// <param name="newItem">The copy.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PostCopyItem(uint flags, nint item, nint destination, nint newName, int result, nint newItem);

    /// <summary>Called before an item is deleted or moved to the Recycle Bin.</summary>
    /// <param name="flags">The <c>TSF_</c> flags of the deletion.</param>
    /// <param name="item">The item.</param>
    /// <returns>The HRESULT; a failure cancels the deletion.</returns>
    [PreserveSig]
    int PreDeleteItem(uint flags, nint item);

    /// <summary>Called after an item is deleted or moved to the Recycle Bin.</summary>
    /// <param name="flags">The <c>TSF_</c> flags of the deletion.</param>
    /// <param name="item">The item.</param>
    /// <param name="result">The result of the deletion.</param>
    /// <param name="newItem">The item in the Recycle Bin, or zero.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PostDeleteItem(uint flags, nint item, int result, nint newItem);

    /// <summary>Called before an item is created.</summary>
    /// <param name="flags">The <c>TSF_</c> flags.</param>
    /// <param name="destination">The destination folder.</param>
    /// <param name="newName">The new name.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PreNewItem(uint flags, nint destination, nint newName);

    /// <summary>Called after an item is created.</summary>
    /// <param name="flags">The <c>TSF_</c> flags.</param>
    /// <param name="destination">The destination folder.</param>
    /// <param name="newName">The new name.</param>
    /// <param name="templateName">The template.</param>
    /// <param name="attributes">The file attributes.</param>
    /// <param name="result">The result of the creation.</param>
    /// <param name="newItem">The new item.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PostNewItem(
        uint flags, nint destination, nint newName, nint templateName, uint attributes, int result, nint newItem);

    /// <summary>Reports the progress.</summary>
    /// <param name="workTotal">The total amount of work.</param>
    /// <param name="workSoFar">The work done.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int UpdateProgress(uint workTotal, uint workSoFar);

    /// <summary>Resets the progress timer.</summary>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int ResetTimer();

    /// <summary>Pauses the progress timer.</summary>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int PauseTimer();

    /// <summary>Resumes the progress timer.</summary>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int ResumeTimer();
}
