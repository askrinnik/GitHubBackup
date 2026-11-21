using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Finds the level a log entry needs to reach one endpoint.
/// </summary>
/// <remarks>
/// The first matching rule wins: an override of the endpoint, a global override, the level of the endpoint, the
/// global default. An override matches a source context that equals its key or starts with the key and a dot,
/// ignoring case; when several keys of one list match, the longest wins. A source of the libraries
/// (<see cref="LibrarySources"/>) that no override matches needs at least <see cref="LibraryFloor"/>, so the debug
/// entries of the libraries stay out of an endpoint that takes the debug entries of the application, while an
/// override of such a source lifts the floor. A <see langword="null"/> level stands for
/// <see cref="LogLevels.Off"/>: the entry is dropped. The result is cached per source context, which is a type name and
/// therefore comes from a small, fixed set.
/// </remarks>
/// <remarks>
/// Initializes the resolver from parsed levels.
/// </remarks>
/// <param name="endpointOverrides">The overrides of the endpoint; <see langword="null"/> drops the source.</param>
/// <param name="globalOverrides">The overrides of <c>Serilog:MinimumLevel:Override</c>.</param>
/// <param name="level">
/// The level of the endpoint, or the global default when the endpoint sets none; <see langword="null"/> drops
/// every entry that no override matches.
/// </param>
internal sealed class EndpointLevelResolver(
    IEnumerable<KeyValuePair<string, LogEventLevel?>> endpointOverrides,
    IEnumerable<KeyValuePair<string, LogEventLevel>> globalOverrides,
    LogEventLevel? level)
{
    /// <summary>The source-context prefixes of the libraries whose entries need at least <see cref="LibraryFloor"/>.</summary>
    public static readonly IReadOnlyList<string> LibrarySources = ["Microsoft", "System"];

    /// <summary>The lowest level an entry of <see cref="LibrarySources"/> needs when no override matches its source.</summary>
    public const LogEventLevel LibraryFloor = LogEventLevel.Information;

    /// <summary>The overrides of the endpoint, longest key first.</summary>
    private readonly KeyValuePair<string, LogEventLevel?>[] _endpointOverrides = [.. endpointOverrides.OrderByDescending(pair => pair.Key.Length)];

    /// <summary>The global overrides, longest key first.</summary>
    private readonly KeyValuePair<string, LogEventLevel>[] _globalOverrides = [.. globalOverrides.OrderByDescending(pair => pair.Key.Length)];

    /// <summary>The level of the endpoint, or the global default when the endpoint sets none.</summary>
    private readonly LogEventLevel? _level = level;

    /// <summary>The resolved levels by source context.</summary>
    private readonly ConcurrentDictionary<string, LogEventLevel?> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the lowest level any entry can need to reach this endpoint, or <see langword="null"/> when every rule
    /// drops entries.
    /// </summary>
    /// <remarks>The library floor only raises a level, so it never lowers this value.</remarks>
    public LogEventLevel? LowestLevel =>
        _endpointOverrides.Select(pair => pair.Value)
            .Concat(_globalOverrides.Select(pair => (LogEventLevel?)pair.Value))
            .Append(_level)
            .Where(level => level is not null)
            .Min();

    /// <summary>
    /// Creates the resolver of one endpoint from validated options.
    /// </summary>
    /// <param name="global">The validated <c>Serilog:MinimumLevel</c> options.</param>
    /// <param name="endpoint">The validated options of the endpoint.</param>
    /// <returns>The resolver.</returns>
    public static EndpointLevelResolver Create(SerilogOptions global, SerilogEndpointOptions endpoint) =>
        new(
            endpoint.Override.Select(pair => KeyValuePair.Create(pair.Key, LogLevels.ParseEndpointLevel(pair.Value ?? string.Empty))),
            global.Override.Select(pair => KeyValuePair.Create(pair.Key, LogLevels.Parse(pair.Value))),
            string.IsNullOrEmpty(endpoint.Level) ? LogLevels.Parse(global.Default) : LogLevels.ParseEndpointLevel(endpoint.Level));

    /// <summary>
    /// Returns the lowest of <paramref name="levels"/>, the threshold of the root logger that lets every entry an
    /// endpoint needs through.
    /// </summary>
    /// <param name="levels">The <see cref="LowestLevel"/> of each attached endpoint.</param>
    /// <returns>The lowest level, or <see cref="LogEventLevel.Fatal"/> when no endpoint needs any entry.</returns>
    public static LogEventLevel Lowest(IEnumerable<LogEventLevel?> levels) =>
        levels.Where(level => level is not null).Min() ?? LogEventLevel.Fatal;

    /// <summary>
    /// Returns the level an entry of <paramref name="sourceContext"/> needs to reach the endpoint.
    /// </summary>
    /// <param name="sourceContext">The source context of the entry, or <see langword="null"/> when it has none.</param>
    /// <returns>The level, or <see langword="null"/> when the endpoint drops the entries of that source.</returns>
    public LogEventLevel? Resolve(string? sourceContext) =>
        sourceContext is null ? _level : _cache.GetOrAdd(sourceContext, ResolveUncached);

    /// <summary>
    /// Returns whether <paramref name="logEvent"/> reaches the endpoint.
    /// </summary>
    /// <param name="logEvent">The entry.</param>
    /// <returns><see langword="true"/> when the level of the entry is not below the level of its source.</returns>
    public bool IsEnabled(LogEvent logEvent)
    {
        var sourceContext =
            logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
            && value is ScalarValue { Value: string text }
                ? text
                : null;
        return Resolve(sourceContext) is { } level && logEvent.Level >= level;
    }

    /// <summary>
    /// Applies the rules to <paramref name="sourceContext"/>.
    /// </summary>
    /// <param name="sourceContext">The source context.</param>
    /// <returns>The level, or <see langword="null"/> when the endpoint drops the entries of that source.</returns>
    private LogEventLevel? ResolveUncached(string sourceContext)
    {
        foreach (var (key, level) in _endpointOverrides)
        {
            if (Matches(sourceContext, key))
            {
                return level;
            }
        }

        foreach (var (key, level) in _globalOverrides)
        {
            if (Matches(sourceContext, key))
            {
                return level;
            }
        }

        return _level is { } endpointLevel && LibrarySources.Any(source => Matches(sourceContext, source))
            ? (endpointLevel < LibraryFloor ? LibraryFloor : endpointLevel)
            : _level;
    }

    /// <summary>
    /// Returns whether the override <paramref name="key"/> applies to <paramref name="sourceContext"/>.
    /// </summary>
    /// <param name="sourceContext">The source context of the entry.</param>
    /// <param name="key">The source-context prefix of the override.</param>
    /// <returns>
    /// <see langword="true"/> when the source context equals the key or continues it after a dot, ignoring case.
    /// </returns>
    private static bool Matches(string sourceContext, string key) =>
        sourceContext.StartsWith(key, StringComparison.OrdinalIgnoreCase)
        && (sourceContext.Length == key.Length || sourceContext[key.Length] == '.');
}
