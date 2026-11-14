using System.Text;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Describes one clone that <see cref="ICloneRelocator"/> moves to another folder.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> leaves <see cref="Token"/> out, so a request written to a log or an exception
/// message never carries the token.
/// </remarks>
public sealed record CloneRelocationRequest
{
    /// <summary>Gets the fully qualified path of the git executable.</summary>
    public required string GitPath { get; init; }

    /// <summary>Gets the absolute folder the clone is in.</summary>
    public required string OldFolder { get; init; }

    /// <summary>Gets the absolute folder the clone moves to.</summary>
    public required string NewFolder { get; init; }

    /// <summary>Gets the URL <c>origin</c> points at after the move, without credentials.</summary>
    public required string RemoteUrl { get; init; }

    /// <summary>
    /// Gets the URL of the repository before a rename, or <see langword="null"/>; the clone is moved only when its
    /// <c>origin</c> points at <see cref="RemoteUrl"/> or at this URL.
    /// </summary>
    public string? PreviousRemoteUrl { get; init; }

    /// <summary>
    /// Gets the configured branch, or <see langword="null"/> for the server's default branch; the check for local work
    /// expects the clone on it.
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Gets the GitHub token sent to the server while the clone is checked, or <see langword="null"/> for anonymous
    /// access.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// Gets a value indicating whether a clone that cannot be moved or repointed is deleted so the next backup clones
    /// it again; only a disposable clone under the clone root may be deleted, never the user's working copy.
    /// </summary>
    public bool DeleteOnFailure { get; init; }

    /// <summary>
    /// Writes every property except <see cref="Token"/> into the text of <see cref="object.ToString"/>.
    /// </summary>
    /// <param name="builder">The builder of the text.</param>
    /// <returns><see langword="true"/>, because members were written.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(GitPath)).Append(" = ").Append(GitPath)
            .Append(", ").Append(nameof(OldFolder)).Append(" = ").Append(OldFolder)
            .Append(", ").Append(nameof(NewFolder)).Append(" = ").Append(NewFolder)
            .Append(", ").Append(nameof(RemoteUrl)).Append(" = ").Append(RemoteUrl)
            .Append(", ").Append(nameof(PreviousRemoteUrl)).Append(" = ").Append(PreviousRemoteUrl)
            .Append(", ").Append(nameof(Branch)).Append(" = ").Append(Branch)
            .Append(", ").Append(nameof(DeleteOnFailure)).Append(" = ").Append(DeleteOnFailure);
        return true;
    }
}
