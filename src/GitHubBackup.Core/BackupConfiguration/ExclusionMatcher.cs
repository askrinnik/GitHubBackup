using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Implements <see cref="IExclusionMatcher"/> with <see cref="WildcardMask"/>.
/// </summary>
public sealed class ExclusionMatcher : IExclusionMatcher
{
    /// <inheritdoc />
    /// <remarks>An account whose URL is not a GitHub account URL is ignored.</remarks>
    public ExclusionMatch? Match(BackupConfig config, string owner, string name)
    {
        foreach (var account in config.Accounts)
        {
            if (!GitHubUrl.TryParseAccount(account.Url, out var login)
                || !string.Equals(login, owner, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var mask in account.Exclude)
            {
                if (WildcardMask.IsMatch(mask, name))
                {
                    return new(mask, ExclusionScope.Account);
                }
            }
        }

        var fullName = $"{owner}/{name}";
        foreach (var mask in config.Exclude)
        {
            if (WildcardMask.IsMatch(mask, fullName))
            {
                return new(mask, ExclusionScope.Global);
            }
        }

        return null;
    }
}
