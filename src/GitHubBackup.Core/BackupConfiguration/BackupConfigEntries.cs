using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Finds and changes repository entries of a <see cref="BackupConfig"/> by the name they are configured under.
/// </summary>
public static class BackupConfigEntries
{
    /// <summary>
    /// Sets the GitHub ID of the entry configured under <paramref name="owner"/> and <paramref name="name"/>.
    /// </summary>
    /// <param name="config">The configuration to change.</param>
    /// <param name="accountLogin">
    /// The login of the tracked account that lists the entry, or <see langword="null"/> for a standalone entry.
    /// </param>
    /// <param name="owner">The login of the owner, ignoring case.</param>
    /// <param name="name">The repository name, ignoring case.</param>
    /// <param name="id">The ID to set.</param>
    /// <returns><see langword="true"/> when the entry was found and changed.</returns>
    public static bool SetId(BackupConfig config, string? accountLogin, string owner, string name, long id)
    {
        if (accountLogin is not null)
        {
            var account = config.Accounts.Find(candidate =>
                GitHubUrl.TryParseAccount(candidate.Url, out var login) && IsSameName(login, accountLogin));
            var entry = account?.Repositories.Find(candidate => IsSameName(candidate.Name, name));
            if (entry is null)
            {
                return false;
            }

            entry.Id = id;
            return true;
        }

        var standalone = config.Repositories.Find(candidate =>
            GitHubUrl.TryParseRepository(candidate.Url, out var candidateOwner, out var candidateName)
            && IsSameName(candidateOwner, owner)
            && IsSameName(candidateName, name));
        if (standalone is null)
        {
            return false;
        }

        standalone.Id = id;
        return true;
    }

    /// <summary>
    /// Compares two names without regard to case.
    /// </summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns><see langword="true"/> when the names are equal ignoring case.</returns>
    private static bool IsSameName(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
