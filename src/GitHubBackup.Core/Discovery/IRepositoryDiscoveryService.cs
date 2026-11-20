using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Compares the repositories GitHub reports with <c>backup-config.json</c> and sorts the differences into classes.
/// </summary>
public interface IRepositoryDiscoveryService
{
    /// <summary>
    /// Collects the repositories of the token's user and of the tracked accounts, removes duplicates by id, and
    /// classifies them against <paramref name="config"/>. The configuration is not changed.
    /// </summary>
    /// <remarks>
    /// Repositories excluded by the <c>exclude</c> masks are never reported as new. A configured repository that is
    /// absent from the collected lists is looked up by name and then by id before it counts as missing. An entry
    /// without an ID is left out of the comparison: it is never missing or renamed, and a collected repository with its
    /// name is not new.
    /// <para>
    /// Without <paramref name="accountLogin"/>, a paused account is left out: its repositories are not listed, its
    /// entries are not compared, and a repository of its owner is never new. The repositories of the token's user are
    /// always listed. Paused entries of other accounts and paused standalone entries are compared as usual.
    /// </para>
    /// </remarks>
    /// <param name="config">A configuration that has passed <see cref="IBackupConfigValidator"/>.</param>
    /// <param name="accountLogin">
    /// The login of one tracked account to discover, ignoring case, paused or not, or <see langword="null"/> for every
    /// account that is not paused and the standalone repositories.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the discovery.</param>
    /// <returns>The classified repositories.</returns>
    /// <exception cref="ArgumentException">
    /// A URL in <paramref name="config"/> is not valid, or <paramref name="accountLogin"/> is not a tracked account.
    /// </exception>
    /// <exception cref="GitHubApiException">Listing the repositories of the user or of an account failed.</exception>
    Task<DiscoveryResult> DiscoverAsync(BackupConfig config, string? accountLogin, CancellationToken cancellationToken);

    /// <summary>
    /// Checks one configured repository with an ID: asks GitHub for it by ID, then by name when the ID answers 404 or
    /// 403, and classifies it as missing, returned or renamed. The configuration is not changed.
    /// </summary>
    /// <remarks>
    /// No new repositories are looked for. A repository found by name counts only when it has the ID of the entry.
    /// <see cref="DiscoveryResult.Repositories"/> holds the repository GitHub reported, when there is one.
    /// </remarks>
    /// <param name="config">A configuration that has passed <see cref="IBackupConfigValidator"/>.</param>
    /// <param name="item">The entry to check; it has an ID.</param>
    /// <param name="cancellationToken">The token that cancels the lookups.</param>
    /// <returns>The classified repository, or a result without findings when nothing changed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> has no ID.</exception>
    /// <exception cref="ArgumentException">
    /// A URL in <paramref name="config"/> is not valid, or no entry of <paramref name="config"/> has the ID of <paramref name="item"/>.
    /// </exception>
    /// <exception cref="GitHubApiException">A lookup failed for another reason, such as an exhausted rate limit.</exception>
    Task<DiscoveryResult> CheckRepositoryAsync(BackupConfig config, BackupWorkItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up each of <paramref name="items"/> that has no ID by its configured name and returns the IDs GitHub
    /// reports. The configuration is not changed.
    /// </summary>
    /// <remarks>
    /// An entry is never looked up by ID, so a resolved ID cannot point at an unrelated repository. An ID that already
    /// belongs to another entry of <paramref name="config"/>, or to an entry resolved earlier in the same call, is a
    /// conflict and is not returned as resolved.
    /// </remarks>
    /// <param name="config">A configuration that has passed <see cref="IBackupConfigValidator"/>.</param>
    /// <param name="items">The entries the run covers; those with an ID are ignored.</param>
    /// <param name="cancellationToken">The token that cancels the lookups.</param>
    /// <returns>The resolved IDs, the entries GitHub answers 404 or 403 for, and the conflicts.</returns>
    /// <exception cref="ArgumentException">A URL in <paramref name="config"/> is not valid.</exception>
    /// <exception cref="GitHubApiException">A lookup failed for another reason, such as an exhausted rate limit.</exception>
    Task<RepositoryIdResolution> ResolveIdsAsync(
        BackupConfig config, IReadOnlyList<BackupWorkItem> items, CancellationToken cancellationToken);
}
