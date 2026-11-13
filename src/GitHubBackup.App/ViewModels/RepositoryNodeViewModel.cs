using System.Globalization;
using GitHubBackup.Core.Overview;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the node of one repository: its name, status and the date of its last archive.
/// </summary>
/// <param name="entry">The repository as read from <c>backup-config.json</c> and its state file.</param>
/// <param name="selectionChanged">Receives the node and its new selection state.</param>
public sealed class RepositoryNodeViewModel(RepositoryOverviewEntry entry, Action<TreeNodeViewModel, bool> selectionChanged)
    : TreeNodeViewModel(CreateKey(entry.Owner, entry.Name), selectionChanged)
{
    /// <summary>The date text of a repository that has no archive yet.</summary>
    public const string NeverText = "Never";

    /// <summary>The date text of a repository whose state file cannot be read.</summary>
    public const string UnknownText = "Unknown";

    /// <summary>The format of the date of the last archive, the same on every machine.</summary>
    public const string DateFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Gets the repository as read from <c>backup-config.json</c> and its state file.</summary>
    public RepositoryOverviewEntry Entry { get; } = entry;

    /// <summary>
    /// Gets the name the tree shows: the repository name under its account, <c>owner/name</c> in the group of
    /// standalone repositories, where owners differ.
    /// </summary>
    public override string DisplayName => Entry.AccountLogin is null ? Entry.FullName : Entry.Name;

    /// <summary>Gets the status of the repository.</summary>
    public RepositoryOverviewStatus Status => Entry.Status;

    /// <summary>Gets whether a run backs up the repository, so a backup of it alone makes sense.</summary>
    public bool IsActive => Entry.Status == RepositoryOverviewStatus.Active;

    /// <summary>
    /// Gets the local time of the last archive in <see cref="DateFormat"/>, <see cref="NeverText"/> when there is
    /// none, or <see cref="UnknownText"/> when the state file cannot be read.
    /// </summary>
    public string LastArchiveText =>
        Entry.StateError is not null ? UnknownText
        : Entry.LastArchiveAt is { } createdAt ? createdAt.ToString(DateFormat, CultureInfo.InvariantCulture)
        : NeverText;

    /// <summary>Gets why the state file cannot be read, or <see langword="null"/>; the tree shows it as a tooltip.</summary>
    public string? StateError => Entry.StateError;

    /// <summary>
    /// Returns the key of the node of a repository; GitHub names ignore case, so the key does too.
    /// </summary>
    /// <param name="owner">The login of the owner.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The key.</returns>
    public static string CreateKey(string owner, string name) =>
        $"repository:{owner.ToUpperInvariant()}/{name.ToUpperInvariant()}";
}
