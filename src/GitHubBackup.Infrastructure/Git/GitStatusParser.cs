namespace GitHubBackup.Infrastructure.Git;

/// <summary>
/// Reads the output of <c>git status --porcelain=v2 --branch</c>.
/// </summary>
internal static class GitStatusParser
{
    /// <summary>The header that names the commit of <c>HEAD</c>.</summary>
    private const string _oidHeader = "# branch.oid ";

    /// <summary>The header that names the checked-out branch.</summary>
    private const string _headHeader = "# branch.head ";

    /// <summary>The value of <see cref="_oidHeader"/> for a branch without a commit.</summary>
    private const string _initialOid = "(initial)";

    /// <summary>The value of <see cref="_headHeader"/> for a detached <c>HEAD</c>.</summary>
    private const string _detachedHead = "(detached)";

    /// <summary>
    /// Parses the lines of the status.
    /// </summary>
    /// <remarks>
    /// Ordinary (<c>1</c>), renamed or copied (<c>2</c>) and unmerged (<c>u</c>) entries are uncommitted changes, a
    /// submodule with new commits, changes or untracked content among them; <c>?</c> entries are untracked files.
    /// Ignored entries and other headers are skipped.
    /// </remarks>
    /// <param name="lines">The lines git wrote to standard output.</param>
    /// <returns>The branch, whether <c>HEAD</c> has a commit, and whether there are changes and untracked files.</returns>
    /// <exception cref="FormatException">The branch headers are missing.</exception>
    public static GitStatusSummary Parse(IReadOnlyList<string> lines)
    {
        string? head = null;
        string? oid = null;
        var changes = false;
        var untracked = false;
        foreach (var line in lines)
        {
            if (line.StartsWith(_headHeader, StringComparison.Ordinal))
            {
                head = line[_headHeader.Length..];
            }
            else if (line.StartsWith(_oidHeader, StringComparison.Ordinal))
            {
                oid = line[_oidHeader.Length..];
            }
            else if (line.StartsWith("1 ", StringComparison.Ordinal)
                || line.StartsWith("2 ", StringComparison.Ordinal)
                || line.StartsWith("u ", StringComparison.Ordinal))
            {
                changes = true;
            }
            else if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                untracked = true;
            }
        }

        if (string.IsNullOrEmpty(head) || string.IsNullOrEmpty(oid))
        {
            throw new FormatException("The status has no branch.head or branch.oid header.");
        }

        return new(
            head == _detachedHead ? null : head,
            oid != _initialOid,
            changes,
            untracked);
    }
}
