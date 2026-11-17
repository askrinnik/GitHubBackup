using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Tests.Telemetry;

/// <summary>
/// Defines the log entries that <see cref="SecretMaskingLogRecordProcessorTests"/> writes.
/// </summary>
internal static partial class ProcessorTestLog
{
    /// <summary>A secret without a recognisable format, so only its registration can mask it.</summary>
    internal const string Secret = "opaque-secret-value-0123";

    /// <summary>
    /// Logs a message whose template holds <see cref="Secret"/> in its literal text.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Header " + Secret + " sent")]
    public static partial void HeaderSent(ILogger logger);

    /// <summary>
    /// Logs a text value and a number.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="value">The text, kept in the <c>Value</c> property.</param>
    /// <param name="count">The number, kept in the <c>Count</c> property.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Value {Value} at {Count}")]
    public static partial void ValueAt(ILogger logger, string value, int count);

    /// <summary>
    /// Logs an address, a property value that is neither text nor a primitive.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="address">The address, kept in the <c>Address</c> property.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Address {Address}")]
    public static partial void Address(ILogger logger, Uri address);

    /// <summary>
    /// Logs a failed request with its exception.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception of the entry.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Request failed")]
    public static partial void RequestFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs the clone of a repository.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository, kept in the <c>Repository</c> property.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Cloning {Repository}")]
    public static partial void Cloning(ILogger logger, string repository);

    /// <summary>
    /// Logs a message at the level given by the caller.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="level">The level of the entry.</param>
    /// <param name="repository">The repository, kept in the <c>Repository</c> property.</param>
    [LoggerMessage(EventId = 6, Message = "Entry for {Repository}")]
    public static partial void AtLevel(ILogger logger, LogLevel level, string repository);

    /// <summary>
    /// Logs an entry with a <c>level</c> property.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="level">The value, kept in the <c>level</c> property.</param>
    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Custom {level}")]
    public static partial void LowerCaseLevelProperty(ILogger logger, string level);

    /// <summary>
    /// Logs an entry with a <c>Level</c> property.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="level">The value, kept in the <c>Level</c> property.</param>
    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Custom {Level}")]
    public static partial void UpperCaseLevelProperty(ILogger logger, string level);
}
