namespace GitHubBackup.Core.Runs;

/// <summary>
/// Names the operation a backup run is busy with, which a host shows while the run works.
/// </summary>
public enum BackupProgressStage
{
    /// <summary>The run lists the repositories of the accounts on GitHub and compares them with the configuration.</summary>
    Discovering,

    /// <summary>The run starts a repository or wiki and reads its state file.</summary>
    Preparing,

    /// <summary>The run reads the refs or the default branch of the server to tell whether anything changed.</summary>
    CheckingRemote,

    /// <summary>The run clones the repository or wiki.</summary>
    Cloning,

    /// <summary>The run fetches the changes of the server into the clone and checks out the branch.</summary>
    Fetching,

    /// <summary>The run creates the archive of the clone.</summary>
    Archiving,
}
