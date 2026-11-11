using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Validates <see cref="GitHubOptions"/>; each failure names the configuration key and never its value.
/// </summary>
internal sealed class GitHubOptionsValidator : IValidateOptions<GitHubOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GitHubOptions options)
    {
        // The token goes with every API request, so plain http is allowed only where it never leaves the machine.
        var isAllowedUri = Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

        return isAllowedUri
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{GitHubOptions.SectionName}:{nameof(GitHubOptions.ApiBaseUrl)} must be an absolute https URI, or http for a loopback address.");
    }
}
