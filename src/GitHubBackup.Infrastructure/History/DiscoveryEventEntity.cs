using GitHubBackup.Core.History;

namespace GitHubBackup.Infrastructure.History;

/// <summary>
/// A row of the <c>DiscoveryEvents</c> table: one discovery event of a run.
/// </summary>
internal sealed class DiscoveryEventEntity
{
    /// <summary>Gets or sets the key of the row, which also keeps the order of the events in the run.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the key of the run the event belongs to.</summary>
    public long RunEntityId { get; set; }

    /// <summary>Gets or sets the repository as <c>owner/name</c>.</summary>
    public required string Repository { get; set; }

    /// <summary>Gets or sets the name before a rename as <c>owner/name</c>, or <see langword="null"/>.</summary>
    public string? PreviousFullName { get; set; }

    /// <summary>Gets or sets what discovery found; stored as the name of the value.</summary>
    public DiscoveryEventKind Kind { get; set; }

    /// <summary>Gets or sets the decision taken, or <see langword="null"/>; stored as the name of the value.</summary>
    public DiscoveryEventDecision? Decision { get; set; }

    /// <summary>Gets or sets who took the decision, or <see langword="null"/>; stored as the name of the value.</summary>
    public DecisionMaker? DecidedBy { get; set; }
}
