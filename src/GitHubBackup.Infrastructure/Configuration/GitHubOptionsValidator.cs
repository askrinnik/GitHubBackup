using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Validates <see cref="GitHubOptions"/>; each failure names the configuration key and never its value.
/// </summary>
internal sealed class GitHubOptionsValidator : IValidateOptions<GitHubOptions>
{
    /// <inheritdoc />
    /// <remarks>The token goes with every API request, so the address must be one that may receive a secret.</remarks>
    public ValidateOptionsResult Validate(string? name, GitHubOptions options) =>
        SecureEndpoint.IsAllowed(options.ApiBaseUrl)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{GitHubOptions.SectionName}:{nameof(GitHubOptions.ApiBaseUrl)} must be an absolute https URI, or http for a loopback address.");
}
