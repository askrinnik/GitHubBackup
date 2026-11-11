namespace GitHubBackup.Cli;

/// <summary>
/// Defines what the command line asks the console application to do.
/// </summary>
internal enum CliCommand
{
    /// <summary>No command: the host starts, checks the settings and stops.</summary>
    None,

    /// <summary>The <c>backup</c> command.</summary>
    Backup,

    /// <summary>An informational option such as <c>--help</c> or <c>--version</c>, answered without starting the host.</summary>
    Information,
}
