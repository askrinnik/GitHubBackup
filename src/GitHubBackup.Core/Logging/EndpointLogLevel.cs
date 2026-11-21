namespace GitHubBackup.Core.Logging;

/// <summary>
/// Names the level a <see cref="LogEndpoint"/> is set to: the least severe entries it receives, or
/// <see cref="Off"/>.
/// </summary>
public enum EndpointLogLevel
{
    /// <summary>Every entry, including the most detailed tracing.</summary>
    Verbose,

    /// <summary>Diagnostic entries and above.</summary>
    Debug,

    /// <summary>Entries about the normal course of a run and above.</summary>
    Information,

    /// <summary>Warnings and above.</summary>
    Warning,

    /// <summary>Errors and above.</summary>
    Error,

    /// <summary>Only failures that stop the application.</summary>
    Fatal,

    /// <summary>No entries: the endpoint is switched off.</summary>
    Off,
}
