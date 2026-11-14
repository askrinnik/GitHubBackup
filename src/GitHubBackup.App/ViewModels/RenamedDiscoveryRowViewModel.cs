using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// The row of a renamed repository in the discovery dialog, with the choice of what happens to its archive folder.
/// </summary>
/// <remarks>
/// The row starts with <see cref="RenameDecision.KeepArchiveFolder"/>, which never moves existing archives. When the
/// archive folder does not depend on the name, both choices lead to the same folder, so the choice is fixed to
/// <see cref="RenameDecision.KeepArchiveFolder"/>.
/// </remarks>
/// <param name="proposal">The renamed repository and its archive folders.</param>
public sealed partial class RenamedDiscoveryRowViewModel(RenameProposal proposal)
    : DiscoveryRowViewModel(
        proposal.Renamed.Current.FullName,
        proposal.Renamed.Current.Id,
        proposal.Renamed.Tracked.AccountLogin,
        proposal.Renamed.Current)
{
    /// <summary>The choices offered for the archive folder.</summary>
    private static readonly IReadOnlyList<DiscoveryOption<RenameDecision>> _variants =
    [
        new(RenameDecision.RenameArchiveFolder, "a) Rename the archive folder"),
        new(RenameDecision.KeepArchiveFolder, "b) Keep the current archive folder"),
    ];

    /// <summary>What happens to the archive folder.</summary>
    [ObservableProperty]
    private RenameDecision _variant = RenameDecision.KeepArchiveFolder;

    /// <summary>Gets the renamed repository and its archive folders.</summary>
    public RenameProposal Proposal { get; } = proposal;

    /// <summary>Gets the choices offered for the archive folder.</summary>
    public IReadOnlyList<DiscoveryOption<RenameDecision>> Variants { get; } = _variants;

    /// <summary>Gets whether the two choices lead to different archive folders, so the user can choose.</summary>
    public bool CanChooseVariant { get; } =
        !ArchiveFolderComparison.IsSameFolder(proposal.CurrentArchiveFolder, proposal.RenamedArchiveFolder);

    /// <summary>Gets the repository under its old name as <c>owner/name</c>.</summary>
    public string OldName => Proposal.Renamed.Tracked.FullName;

    /// <summary>Gets the archive folder the entry resolves to now.</summary>
    public string CurrentArchiveFolder => Proposal.CurrentArchiveFolder;

    /// <summary>Gets the archive folder after <see cref="RenameDecision.RenameArchiveFolder"/>.</summary>
    public string RenamedArchiveFolder => Proposal.RenamedArchiveFolder;

    /// <inheritdoc />
    public override string Kind => "Renamed";

    /// <inheritdoc />
    public override string Note => CanChooseVariant
        ? $"Renamed from {OldName}; the clone moves to the new name"
        : $"Renamed from {OldName}; the archive folder does not depend on the name";

    /// <inheritdoc />
    public override bool IsActionable => true;

    /// <summary>
    /// Returns the decision the row stands for.
    /// </summary>
    /// <returns><see cref="Variant"/>, or <see cref="RenameDecision.KeepArchiveFolder"/> when the user cannot choose.</returns>
    public RenameDecision ToDecision() => CanChooseVariant ? Variant : RenameDecision.KeepArchiveFolder;

    /// <summary>
    /// Keeps the choice at <see cref="RenameDecision.KeepArchiveFolder"/> when the user cannot choose.
    /// </summary>
    /// <param name="value">The new choice.</param>
    partial void OnVariantChanged(RenameDecision value)
    {
        if (!CanChooseVariant && value != RenameDecision.KeepArchiveFolder)
        {
            Variant = RenameDecision.KeepArchiveFolder;
        }
    }
}
