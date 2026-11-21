using GitHubBackup.Core.Logging;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One entry of a level list of the settings editor.
/// </summary>
/// <param name="Value">The level, or <see langword="null"/> for the default level of the application.</param>
/// <param name="Label">The text the list shows.</param>
public sealed record LogLevelOption(EndpointLogLevel? Value, string Label)
{
    /// <summary>
    /// Returns the entries of a level list: the default first, labelled with <paramref name="defaultLevel"/>, then
    /// every level from the most detailed to <see cref="EndpointLogLevel.Off"/>.
    /// </summary>
    /// <param name="defaultLevel">The default level of the running application.</param>
    /// <returns>The entries, in order.</returns>
    public static IReadOnlyList<LogLevelOption> Create(EndpointLogLevel defaultLevel) =>
    [
        new(null, $"Default ({defaultLevel})"),
        .. Enum.GetValues<EndpointLogLevel>().Select(level => new LogLevelOption(level, level.ToString())),
    ];

    /// <inheritdoc />
    public override string ToString() => Label;
}
