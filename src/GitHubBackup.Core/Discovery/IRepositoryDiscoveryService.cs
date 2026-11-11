using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.GitHub;

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
    /// absent from the collected lists is looked up by name and then by id before it counts as missing.
    /// </remarks>
    /// <param name="config">A configuration that has passed <see cref="IBackupConfigValidator"/>.</param>
    /// <param name="accountLogin">
    /// The login of one tracked account to discover, ignoring case, or <see langword="null"/> for every account and the
    /// standalone repositories.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the discovery.</param>
    /// <returns>The classified repositories.</returns>
    /// <exception cref="ArgumentException">
    /// A URL in <paramref name="config"/> is not valid, or <paramref name="accountLogin"/> is not a tracked account.
    /// </exception>
    /// <exception cref="GitHubApiException">Listing the repositories of the user or of an account failed.</exception>
    Task<DiscoveryResult> DiscoverAsync(BackupConfig config, string? accountLogin, CancellationToken cancellationToken);
}
