using Serilog.Events;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Names and parses the log levels accepted in configuration.
/// </summary>
/// <remarks>
/// Names are compared ignoring case, and numeric values are rejected although <see cref="Enum.Parse{TEnum}(string, bool)"/>
/// accepts them. <see cref="Off"/> is valid only for a log endpoint, where it drops entries instead of naming a level.
/// </remarks>
internal static class LogLevels
{
    /// <summary>The name that switches an endpoint, or a source of an endpoint, off.</summary>
    public const string Off = "Off";

    /// <summary>Gets the level names of <c>Serilog:MinimumLevel</c>, from the most to the least detailed.</summary>
    public static IReadOnlyList<string> Names { get; } = Enum.GetNames<LogEventLevel>();

    /// <summary>Gets the level names of a log endpoint: <see cref="Names"/> followed by <see cref="Off"/>.</summary>
    public static IReadOnlyList<string> EndpointNames { get; } = [.. Names, Off];

    /// <summary>
    /// Returns whether <paramref name="value"/> is one of <see cref="Names"/>.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <returns><see langword="true"/> when the value names a level.</returns>
    public static bool IsName(string? value) => Names.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns whether <paramref name="value"/> is one of <see cref="EndpointNames"/>.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <returns><see langword="true"/> when the value names a level or <see cref="Off"/>.</returns>
    public static bool IsEndpointName(string? value) => EndpointNames.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Converts a name accepted by <see cref="IsName"/> to a <see cref="LogEventLevel"/>.
    /// </summary>
    /// <param name="value">The validated name.</param>
    /// <returns>The level.</returns>
    public static LogEventLevel Parse(string? value) => Enum.Parse<LogEventLevel>(value ?? string.Empty, ignoreCase: true);

    /// <summary>
    /// Converts a name accepted by <see cref="IsEndpointName"/> to a <see cref="LogEventLevel"/>.
    /// </summary>
    /// <param name="value">The validated name.</param>
    /// <returns>The level, or <see langword="null"/> for <see cref="Off"/>.</returns>
    public static LogEventLevel? ParseEndpointLevel(string value) =>
        string.Equals(value, Off, StringComparison.OrdinalIgnoreCase) ? null : Parse(value);
}
