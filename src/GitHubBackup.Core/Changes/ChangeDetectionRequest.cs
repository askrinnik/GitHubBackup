using GitHubBackup.Core.Git;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Changes;

/// <summary>
/// Describes the repository or wiki that <see cref="IChangeDetector"/> checks.
/// </summary>
public sealed record ChangeDetectionRequest
{
    /// <summary>Gets the git executable, the clone folder, the branch and the token.</summary>
    public required GitRepositoryRequest Git { get; init; }

    /// <summary>Gets the URL of the repository or wiki on the server, without credentials.</summary>
    public required string RemoteUrl { get; init; }

    /// <summary>Gets the fully qualified path of the folder that holds the archives of this target.</summary>
    public required string ArchiveFolder { get; init; }

    /// <summary>Gets the last archive recorded for this target, or <see langword="null"/> when there is none.</summary>
    public ArchiveRecord? LastArchive { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="IChangeDetector.PreviewAsync"/> asks the server for the refs of a
    /// target that has no clone, instead of reporting <see cref="ChangeDecision.CloneNeeded"/> without contacting it.
    /// </summary>
    public bool CheckRemoteWithoutClone { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server may have no repository at <see cref="RemoteUrl"/>, as for a wiki
    /// that is enabled but was never written; the caller handles <see cref="GitRepositoryNotFoundException"/>, so
    /// the operation spans are not marked failed for it.
    /// </summary>
    public bool RemoteMayBeMissing { get; init; }

    /// <summary>
    /// Gets the receiver of each git operation the detector starts: <see cref="BackupProgressStage.CheckingRemote"/>,
    /// <see cref="BackupProgressStage.Cloning"/> or <see cref="BackupProgressStage.Fetching"/>; <see langword="null"/>
    /// when nobody follows the progress.
    /// </summary>
    public IProgress<BackupProgressStage>? OperationProgress { get; init; }
}
