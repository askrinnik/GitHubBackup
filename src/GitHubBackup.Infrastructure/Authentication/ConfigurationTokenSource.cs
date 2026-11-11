using GitHubBackup.Core.Authentication;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Authentication;

/// <summary>
/// Reads the token from the <see cref="GitHubOptions.TokenKey"/> configuration key, which covers
/// <c>appsettings.json</c>, environment variables, user secrets and the command line.
/// </summary>
/// <param name="configuration">The application configuration.</param>
internal sealed class ConfigurationTokenSource(IConfiguration configuration) : ITokenSource
{
    /// <inheritdoc />
    public string Name => "configuration (GitHub:Token)";

    /// <inheritdoc />
    public Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var value = configuration[GitHubOptions.TokenKey];
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
}
