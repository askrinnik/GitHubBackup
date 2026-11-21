namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Holds the settings of the <see cref="ILiveLogFeed"/> that the host sets in code; they are not read from the
/// configuration.
/// </summary>
public sealed class LiveLogOptions
{
    /// <summary>The largest <see cref="BufferCapacity"/> the validation accepts.</summary>
    public const int MaxBufferCapacity = 100_000;

    /// <summary>
    /// Gets or sets the number of the most recent entries the feed keeps for a new subscriber, from
    /// <c>0</c> to <see cref="MaxBufferCapacity"/>.
    /// </summary>
    /// <remarks>
    /// <c>0</c> keeps no entries: a host without a window that shows the feed does not render and mask each entry for
    /// nobody.
    /// </remarks>
    public int BufferCapacity { get; set; }
}
