namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Names the action the user chooses for a new repository in the discovery dialog.
/// </summary>
public enum NewRepositoryChoice
{
    /// <summary>Adds the repository with the branch and the archive folder of its row.</summary>
    Add,

    /// <summary>Leaves the configuration unchanged; the next run offers the repository again.</summary>
    Skip,

    /// <summary>Excludes the repository by its name in the masks of its account.</summary>
    ExcludeFromAccount,

    /// <summary>Excludes the repository by <c>owner/name</c> in the global masks.</summary>
    ExcludeGlobally,
}
