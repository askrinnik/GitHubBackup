namespace GitHubBackup.App.ViewModels;

/// <summary>
/// A section of the settings editor, in the order of its navigation.
/// </summary>
public enum SettingsSection
{
    /// <summary>The roots, the global exclusion masks, discovery of other owners and the theme.</summary>
    General,

    /// <summary>The tracked accounts and their repositories.</summary>
    Accounts,

    /// <summary>The standalone repositories.</summary>
    Repositories,

    /// <summary>The paths of git and 7-Zip.</summary>
    Tools,

    /// <summary>The OpenTelemetry settings.</summary>
    Telemetry,

    /// <summary>The levels of the log endpoints.</summary>
    Logging,

    /// <summary>The GitHub token and its check.</summary>
    Token,
}
