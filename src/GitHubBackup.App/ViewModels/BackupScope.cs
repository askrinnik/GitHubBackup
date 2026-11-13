namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Names which repositories a backup started from the main window covers.
/// </summary>
public enum BackupScope
{
    /// <summary>Every repository of <c>backup-config.json</c>.</summary>
    All,

    /// <summary>The repositories of the selected account, or of the account of the selected repository.</summary>
    Account,

    /// <summary>The selected repository.</summary>
    Repository,
}
