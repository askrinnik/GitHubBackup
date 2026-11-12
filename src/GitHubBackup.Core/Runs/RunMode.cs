namespace GitHubBackup.Core.Runs;

/// <summary>
/// Defines how a backup run was started and who answers its questions.
/// </summary>
public enum RunMode
{
    /// <summary>The console application asks the user in the terminal.</summary>
    Interactive,

    /// <summary>The console application asks nothing; every decision is made automatically.</summary>
    Silent,

    /// <summary>The WPF application asks the user in dialogs.</summary>
    Ui,
}
