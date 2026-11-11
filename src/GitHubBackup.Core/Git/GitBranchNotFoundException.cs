namespace GitHubBackup.Core.Git;

/// <summary>
/// Reports that the server has branches, but neither the requested branch nor the server's default branch exists
/// among them, so no branch can be checked out.
/// </summary>
/// <param name="message">The message, naming the clone folder and the branches involved.</param>
/// <param name="requestedBranch">The requested branch, or <see langword="null"/> when none was requested.</param>
/// <param name="defaultBranch">
/// The branch the server's <c>HEAD</c> names, or <see langword="null"/> when it names no branch.
/// </param>
public sealed class GitBranchNotFoundException(string message, string? requestedBranch, string? defaultBranch)
    : Exception(message)
{
    /// <summary>Gets the requested branch, or <see langword="null"/> when none was requested.</summary>
    public string? RequestedBranch { get; } = requestedBranch;

    /// <summary>Gets the branch the server's <c>HEAD</c> names, or <see langword="null"/> when it names none.</summary>
    public string? DefaultBranch { get; } = defaultBranch;
}
