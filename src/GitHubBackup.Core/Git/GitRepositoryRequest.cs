using System.Text;

namespace GitHubBackup.Core.Git;

/// <summary>
/// Describes the local clone that one call of <see cref="IGitClient"/> works on.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> leaves <see cref="Token"/> out, so a request written to a log or an exception
/// message never carries the token.
/// </remarks>
public sealed record GitRepositoryRequest
{
    /// <summary>Gets the fully qualified path of the git executable.</summary>
    public required string GitPath { get; init; }

    /// <summary>Gets the fully qualified path of the clone folder.</summary>
    public required string ClonePath { get; init; }

    /// <summary>
    /// Gets the branch the clone checks out, or <see langword="null"/> for the server's default branch; a branch must
    /// not be empty or start with <c>-</c>.
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Gets the GitHub token sent to the server, or <see langword="null"/> to access the repository anonymously.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// Writes every property except <see cref="Token"/> into the text of <see cref="object.ToString"/>.
    /// </summary>
    /// <param name="builder">The builder of the text.</param>
    /// <returns><see langword="true"/>, because members were written.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(GitPath)).Append(" = ").Append(GitPath)
            .Append(", ").Append(nameof(ClonePath)).Append(" = ").Append(ClonePath)
            .Append(", ").Append(nameof(Branch)).Append(" = ").Append(Branch);
        return true;
    }
}
