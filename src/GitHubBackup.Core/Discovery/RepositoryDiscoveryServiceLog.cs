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

    /// <summary>
    /// Logs the ID found by name for an entry without one.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The entry as <c>owner/name</c>.</param>
    /// <param name="id">The ID GitHub reported.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Found id {Id} for {Repository}, which has no id in backup-config.json")]
    public static partial void IdResolved(ILogger logger, string repository, long id);

    /// <summary>
    /// Logs an entry without an ID that GitHub does not know or does not show.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The entry as <c>owner/name</c>.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "{Repository} has no id in backup-config.json and was not found on GitHub")]
    public static partial void IdNotFound(ILogger logger, string repository);

    /// <summary>
    /// Logs an entry without an ID whose ID, found by name, already belongs to another entry.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The entry as <c>owner/name</c>.</param>
    /// <param name="id">The ID GitHub reported.</param>
    /// <param name="otherRepository">The other entry as <c>owner/name</c>.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "The id {Id} found for {Repository} already belongs to {OtherRepository} in backup-config.json")]
    public static partial void IdConflict(ILogger logger, string repository, long id, string otherRepository);
}
