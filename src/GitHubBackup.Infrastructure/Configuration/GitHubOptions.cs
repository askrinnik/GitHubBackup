namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Holds the <c>GitHub</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <c>GitHub:Token</c> is deliberately not a property: an options object can end up in a log or an exception
/// message, so the token is read from <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> only where it is used.
/// </remarks>
public sealed class GitHubOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "GitHub";

    /// <summary>Gets or sets the base URL of the GitHub REST API.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";
}
