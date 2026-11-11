using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Defines the log entries of <see cref="RepositoryDiscoveryService"/>.
/// </summary>
internal static partial class RepositoryDiscoveryServiceLog
{
    /// <summary>
    /// Logs the number of repositories one source returned.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="source">The source: <c>user</c> or the login of an account.</param>
    /// <param name="count">The number of repositories.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Discovery source {Source} returned {Count} repositories")]
    public static partial void SourceListed(ILogger logger, string source, int count);

    /// <summary>
    /// Logs the number of repositories in each class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="newInAccounts">The number of new repositories in tracked accounts.</param>
    /// <param name="newStandalone">The number of new standalone repositories.</param>
    /// <param name="missing">The number of missing repositories.</param>
    /// <param name="returned">The number of returned repositories.</param>
    /// <param name="renamed">The number of renamed repositories.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Discovery found {NewInAccounts} new in accounts, {NewStandalone} new standalone, {Missing} missing, {Returned} returned, {Renamed} renamed")]
    public static partial void Classified(
        ILogger logger, int newInAccounts, int newStandalone, int missing, int returned, int renamed);

    /// <summary>
    /// Logs a repository that discovery ignores because its owner or name cannot be a folder name.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The <c>owner/name</c> GitHub reported.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Discovery ignores {Repository}: its owner or name is not valid as a folder name")]
    public static partial void InvalidName(ILogger logger, string repository);
}
