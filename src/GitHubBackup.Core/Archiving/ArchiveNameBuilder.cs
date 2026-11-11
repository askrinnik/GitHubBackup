using System.Globalization;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Implements <see cref="IArchiveNameBuilder"/> with the local time of the injected clock.
/// </summary>
/// <param name="timeProvider">The clock; its local time enters the name.</param>
/// <param name="options">Supplies the length of the hash in the name.</param>
public sealed class ArchiveNameBuilder(TimeProvider timeProvider, IOptions<BackupOptions> options) : IArchiveNameBuilder
{
    /// <summary>The format of the time in the name.</summary>
    private const string _timeFormat = "yyyyMMdd-HHmmss";

    /// <inheritdoc />
    public ArchiveName Build(string owner, string repository, BackupTarget target, string headCommit)
    {
        if (!GitHubUrl.IsValidLogin(owner))
        {
            throw new ArgumentException($"'{owner}' is not a valid GitHub login.", nameof(owner));
        }

        if (!GitHubUrl.IsValidRepositoryName(repository))
        {
            throw new ArgumentException($"'{repository}' is not a valid GitHub repository name.", nameof(repository));
        }

        var length = options.Value.ShortHashLength;
        if (headCommit is null || headCommit.Length < length || !headCommit.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException(
                $"The commit id must be hexadecimal with at least {length} characters.", nameof(headCommit));
        }

        var createdAt = timeProvider.GetLocalNow();
        var suffix = target == BackupTarget.Wiki ? ".wiki" : string.Empty;
        var time = createdAt.ToString(_timeFormat, CultureInfo.InvariantCulture);
        var hash = headCommit[..length].ToLowerInvariant();
        return new ArchiveName($"{owner}_{repository}{suffix}_{time}_{hash}.zip", createdAt);
    }
}
