using GitHubBackup.Core;
using GitHubBackup.Core.Runs;

namespace GitHubBackup.Infrastructure.History;

/// <summary>
/// A row of the <c>Runs</c> table: one backup run.
/// </summary>
internal sealed class RunEntity
{
    /// <summary>Gets or sets the key of the row.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the identifier of the run, unique in the table.</summary>
    public Guid RunId { get; set; }

    /// <summary>Gets or sets the local time the run started, with its offset.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Gets or sets the local time the run finished, with its offset.</summary>
    public DateTimeOffset FinishedAt { get; set; }

    /// <summary>Gets or sets how the run was started; stored as the name of the value.</summary>
    public RunMode Mode { get; set; }

    /// <summary>Gets or sets whether the run was a dry run.</summary>
    public bool IsDryRun { get; set; }

    /// <summary>Gets or sets the login of the account the run was limited to, or <see langword="null"/>.</summary>
    public string? AccountFilter { get; set; }

    /// <summary>Gets or sets the repository the run was limited to as <c>owner/name</c>, or <see langword="null"/>.</summary>
    public string? RepositoryFilter { get; set; }

    /// <summary>Gets or sets the exit code of the run; stored as its number.</summary>
    public ExitCode ExitCode { get; set; }

    /// <summary>Gets the items of the run.</summary>
    public List<RunItemEntity> Items { get; } = [];

    /// <summary>Gets the discovery events of the run.</summary>
    public List<DiscoveryEventEntity> DiscoveryEvents { get; } = [];
}
