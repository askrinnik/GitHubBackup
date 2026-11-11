using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// The answer of an <see cref="IDiscoveryDecisionHandler"/> for one new repository.
/// </summary>
/// <param name="Action">What happens to the repository.</param>
/// <param name="Branch">
/// The branch written to the new entry with <see cref="NewRepositoryAction.Add"/>, or <see langword="null"/> for the
/// default branch.
/// </param>
/// <param name="Path">
/// The absolute archive folder written to the new entry with <see cref="NewRepositoryAction.Add"/>, or
/// <see langword="null"/> for the folder derived from the configuration.
/// </param>
/// <param name="ExclusionScope">The list that receives the mask with <see cref="NewRepositoryAction.Exclude"/>.</param>
public sealed record NewRepositoryDecision(
    NewRepositoryAction Action, string? Branch, string? Path, ExclusionScope? ExclusionScope)
{
    /// <summary>Gets the decision that leaves the configuration unchanged.</summary>
    public static NewRepositoryDecision Skip { get; } = new(NewRepositoryAction.Skip, null, null, null);

    /// <summary>
    /// Returns the decision that adds the repository.
    /// </summary>
    /// <param name="branch">The branch, or <see langword="null"/> for the default branch.</param>
    /// <param name="path">The absolute archive folder, or <see langword="null"/> for the derived folder.</param>
    /// <returns>The decision.</returns>
    public static NewRepositoryDecision Add(string? branch = null, string? path = null) =>
        new(NewRepositoryAction.Add, branch, path, null);

    /// <summary>
    /// Returns the decision that excludes the repository with a mask in <paramref name="scope"/>.
    /// </summary>
    /// <param name="scope">
    /// <see cref="ExclusionScope.Account"/> adds the repository name to the masks of its account;
    /// <see cref="ExclusionScope.Global"/> adds <c>owner/name</c> to the global masks.
    /// </param>
    /// <returns>The decision.</returns>
    public static NewRepositoryDecision Exclude(ExclusionScope scope) =>
        new(NewRepositoryAction.Exclude, null, null, scope);
}
