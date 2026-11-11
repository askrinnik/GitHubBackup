namespace GitHubBackup.Core.Runs;

/// <summary>
/// Limits a backup run to every repository of <c>backup-config.json</c>, to the entries of one tracked account, or to
/// one repository.
/// </summary>
/// <remarks>
/// A run limited to one repository skips discovery; a run limited to one account discovers only that account and
/// processes only the entries listed under it, not the standalone repositories of the same owner.
/// </remarks>
public sealed record BackupRunFilter
{
    /// <summary>
    /// Initializes the filter.
    /// </summary>
    /// <param name="accountLogin">The login of the account, or <see langword="null"/>.</param>
    /// <param name="repositoryOwner">The owner of the repository, or <see langword="null"/>.</param>
    /// <param name="repositoryName">The name of the repository, or <see langword="null"/>.</param>
    private BackupRunFilter(string? accountLogin, string? repositoryOwner, string? repositoryName)
    {
        AccountLogin = accountLogin;
        RepositoryOwner = repositoryOwner;
        RepositoryName = repositoryName;
    }

    /// <summary>Gets the filter that keeps every repository and discovers every account.</summary>
    public static BackupRunFilter All { get; } = new(null, null, null);

    /// <summary>Gets the login of the account the run is limited to, or <see langword="null"/>.</summary>
    public string? AccountLogin { get; }

    /// <summary>Gets the owner of the repository the run is limited to, or <see langword="null"/>.</summary>
    public string? RepositoryOwner { get; }

    /// <summary>Gets the name of the repository the run is limited to, or <see langword="null"/>.</summary>
    public string? RepositoryName { get; }

    /// <summary>Gets whether the run is limited to one repository and therefore skips discovery.</summary>
    public bool IsRepository => RepositoryName is not null;

    /// <summary>Gets the repository the run is limited to as <c>owner/name</c>, or <see langword="null"/>.</summary>
    public string? RepositoryFullName => IsRepository ? $"{RepositoryOwner}/{RepositoryName}" : null;

    /// <summary>
    /// Returns the filter that limits the run to the entries of one tracked account.
    /// </summary>
    /// <param name="login">The login of the account, compared without regard to case.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentException"><paramref name="login"/> is empty or white space.</exception>
    public static BackupRunFilter ForAccount(string login)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        return new(login, null, null);
    }

    /// <summary>
    /// Returns the filter that limits the run to one repository of <c>backup-config.json</c>.
    /// </summary>
    /// <param name="owner">The login of the owner, compared without regard to case.</param>
    /// <param name="name">The repository name, compared without regard to case.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentException"><paramref name="owner"/> or <paramref name="name"/> is empty or white space.</exception>
    public static BackupRunFilter ForRepository(string owner, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(null, owner, name);
    }

    /// <summary>
    /// Returns whether the run processes a repository of the configuration.
    /// </summary>
    /// <param name="item">The repository.</param>
    /// <returns><see langword="true"/> when the filter keeps <paramref name="item"/>.</returns>
    public bool Includes(BackupWorkItem item)
    {
        if (IsRepository)
        {
            return IsSameName(item.Owner, RepositoryOwner!) && IsSameName(item.Name, RepositoryName!);
        }

        return AccountLogin is null || (item.AccountLogin is not null && IsSameName(item.AccountLogin, AccountLogin));
    }

    /// <summary>
    /// Compares two logins or names without regard to case, as GitHub does.
    /// </summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns><see langword="true"/> when the names are equal ignoring case.</returns>
    private static bool IsSameName(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
