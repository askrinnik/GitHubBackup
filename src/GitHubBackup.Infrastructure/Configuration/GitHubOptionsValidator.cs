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
        var isHttpUri = Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        return isHttpUri
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{GitHubOptions.SectionName}:{nameof(GitHubOptions.ApiBaseUrl)} must be an absolute http or https URI.");
    }
}
