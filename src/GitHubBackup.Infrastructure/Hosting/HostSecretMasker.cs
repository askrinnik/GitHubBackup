using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Creates the <see cref="ISecretMasker"/> of a host, which knows the token the configuration sets.
/// </summary>
public static class HostSecretMasker
{
    /// <summary>
    /// Creates a masker with the value of <see cref="GitHubOptions.TokenKey"/> registered when the configuration sets
    /// one that is not empty or white space.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The masker.</returns>
    public static ISecretMasker Create(IConfiguration configuration)
    {
        var masker = new SecretMasker();
        var token = configuration[GitHubOptions.TokenKey];
        if (!string.IsNullOrWhiteSpace(token))
        {
            masker.Register(token);
        }

        return masker;
    }
}
