namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Collects the warnings of configuration providers, which load before logging exists, so that
/// <see cref="ConfigurationWarningsLogger"/> logs them when the host starts.
/// </summary>
/// <remarks>A warning never contains the value of a secret.</remarks>
internal sealed class ConfigurationWarnings
{
    /// <summary>The warnings in the order they were added.</summary>
    private readonly List<string> _items = [];

    /// <summary>Guards <see cref="_items"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>Gets a copy of the warnings in the order they were added.</summary>
    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    /// <summary>
    /// Adds a warning.
    /// </summary>
    /// <param name="warning">The text of the warning.</param>
    public void Add(string warning)
    {
        lock (_gate)
        {
            _items.Add(warning);
        }
    }
}
