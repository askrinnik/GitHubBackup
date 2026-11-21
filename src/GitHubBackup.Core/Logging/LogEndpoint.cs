namespace GitHubBackup.Core.Logging;

/// <summary>
/// Names the outputs that receive log entries; each output has its own levels.
/// </summary>
public enum LogEndpoint
{
    /// <summary>The daily text and structured log files.</summary>
    File,

    /// <summary>The standard error of the console application.</summary>
    Console,

    /// <summary>The live log feed of the WPF application.</summary>
    LiveLog,

    /// <summary>The OTLP export of the log entries, when telemetry is enabled.</summary>
    OpenTelemetry,
}
