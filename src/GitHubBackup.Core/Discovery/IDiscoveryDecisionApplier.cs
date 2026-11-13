using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Applies a <see cref="DiscoveryResult"/> and the decisions of an <see cref="IDiscoveryDecisionHandler"/> to the
/// configuration, the archive folders and the clones.
/// </summary>
public interface IDiscoveryDecisionApplier
{
    /// <summary>
    /// Shows <paramref name="handler"/> every finding through <see cref="IDiscoveryDecisionHandler.ReviewAsync"/>,
    /// asks it about every new and renamed repository, changes <paramref name="config"/>,
    /// saves it through <see cref="IBackupConfigStore"/> when anything changed, and moves the clones of renamed
    /// repositories.
    /// </summary>
    /// <remarks>
    /// Missing entries become <see cref="RepositoryStatus.Unavailable"/> and returned ones
    /// <see cref="RepositoryStatus.Active"/> without a question; nothing of a missing repository is deleted. Every
    /// decision is collected before anything changes, so a cancelled decision leaves the configuration and the disk
    /// untouched. Archive folders are renamed before the save and renamed back when the save fails; clones are moved
    /// only after a successful save. When the save fails, <paramref name="config"/> holds the unsaved changes and must
    /// be loaded again.
    /// </remarks>
    /// <param name="config">The configuration that <paramref name="discovery"/> was computed from.</param>
    /// <param name="discovery">The classified repositories.</param>
    /// <param name="handler">Decides about new and renamed repositories.</param>
    /// <param name="gitPath">The fully qualified path of the git executable.</param>
    /// <param name="cancellationToken">Cancels the decisions, the save and the clone moves.</param>
    /// <returns>What was changed.</returns>
    /// <exception cref="ArgumentException">
    /// A decision excludes a standalone repository with <see cref="ExclusionScope.Account"/>, or a repository of
    /// <paramref name="discovery"/> is not in <paramref name="config"/>.
    /// </exception>
    /// <exception cref="BackupConfigException">The changed configuration fails validation; nothing is written.</exception>
    /// <exception cref="OperationCanceledException">The handler or <paramref name="cancellationToken"/> cancelled.</exception>
    Task<DiscoveryApplyResult> ApplyAsync(
        BackupConfig config,
        DiscoveryResult discovery,
        IDiscoveryDecisionHandler handler,
        string gitPath,
        CancellationToken cancellationToken);
}
