using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One row of the discovery dialog: a repository that discovery found new, missing, renamed or returned, with the
/// GitHub attributes the dialog shows for it.
/// </summary>
/// <remarks>
/// The attributes from GitHub are empty texts for a repository that GitHub does not report.
/// </remarks>
public abstract class DiscoveryRowViewModel : ObservableObject
{
    /// <summary>The text shown for the account of a repository that no tracked account lists.</summary>
    public const string StandaloneLabel = "standalone";

    /// <summary>
    /// Initializes the row.
    /// </summary>
    /// <param name="fullName">The repository as <c>owner/name</c>.</param>
    /// <param name="id">The GitHub id.</param>
    /// <param name="accountLogin">The login of the tracked account, or <see langword="null"/> for a standalone repository.</param>
    /// <param name="repository">The repository as GitHub reports it, or <see langword="null"/> when GitHub does not report it.</param>
    protected DiscoveryRowViewModel(string fullName, long id, string? accountLogin, GitHubRepository? repository)
    {
        Repository = fullName;
        Id = id.ToString(CultureInfo.InvariantCulture);
        Account = accountLogin ?? StandaloneLabel;
        if (repository is not null)
        {
            Url = repository.HtmlUrl;
            Owner = repository.Owner;
            Visibility = repository.IsPrivate ? "Private" : "Public";
            Fork = YesNo(repository.IsFork);
            Archived = YesNo(repository.IsArchived);
            DefaultBranch = repository.DefaultBranch;
            Wiki = YesNo(repository.HasWiki);
        }
    }

    /// <summary>Gets the kind of finding: <c>New</c>, <c>Renamed</c>, <c>Missing</c> or <c>Returned</c>.</summary>
    public abstract string Kind { get; }

    /// <summary>Gets what happens to the repository when nothing is chosen for it, or an empty text.</summary>
    public abstract string Note { get; }

    /// <summary>Gets whether the user chooses an action for the row.</summary>
    public abstract bool IsActionable { get; }

    /// <summary>Gets whether a value of the row is not valid, so the decisions cannot be applied.</summary>
    public virtual bool HasErrors => false;

    /// <summary>Gets the repository as <c>owner/name</c>, under its current name when GitHub reports it.</summary>
    public string Repository { get; }

    /// <summary>Gets the GitHub id.</summary>
    public string Id { get; }

    /// <summary>Gets the login of the tracked account, or <see cref="StandaloneLabel"/>.</summary>
    public string Account { get; }

    /// <summary>Gets the web address of the repository.</summary>
    public string Url { get; } = string.Empty;

    /// <summary>Gets the login of the owner.</summary>
    public string Owner { get; } = string.Empty;

    /// <summary>Gets <c>Private</c> or <c>Public</c>.</summary>
    public string Visibility { get; } = string.Empty;

    /// <summary>Gets whether the repository is a fork, as <c>Yes</c> or <c>No</c>.</summary>
    public string Fork { get; } = string.Empty;

    /// <summary>Gets whether the repository is archived, as <c>Yes</c> or <c>No</c>.</summary>
    public string Archived { get; } = string.Empty;

    /// <summary>Gets the default branch.</summary>
    public string DefaultBranch { get; } = string.Empty;

    /// <summary>Gets whether the wiki feature is enabled, as <c>Yes</c> or <c>No</c>.</summary>
    public string Wiki { get; } = string.Empty;

    /// <summary>
    /// Returns <c>Yes</c> or <c>No</c>.
    /// </summary>
    /// <param name="value">The flag.</param>
    /// <returns>The word.</returns>
    private static string YesNo(bool value) => value ? "Yes" : "No";
}
