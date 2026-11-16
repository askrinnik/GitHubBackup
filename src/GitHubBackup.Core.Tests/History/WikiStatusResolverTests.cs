using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Tests.History;

/// <summary>
/// Tests <see cref="WikiStatusResolver"/> over every row of the wiki status table.
/// </summary>
public sealed class WikiStatusResolverTests
{
    /// <summary>Gets every wiki item status with the status it resolves to.</summary>
    public static TheoryData<BackupItemStatus, WikiStatus> WikiItems => new()
    {
        { BackupItemStatus.Archived, WikiStatus.Ok },
        { BackupItemStatus.UpToDate, WikiStatus.Ok },
        { BackupItemStatus.Warning, WikiStatus.Ok },
        { BackupItemStatus.WouldArchive, WikiStatus.Ok },
        { BackupItemStatus.WouldClone, WikiStatus.Ok },
        { BackupItemStatus.Skipped, WikiStatus.NoPages },
        { BackupItemStatus.Failed, WikiStatus.Failed },
        { BackupItemStatus.Cancelled, WikiStatus.Cancelled },
    };

    /// <summary>Gets every repository item status of a run without a wiki item with the status it resolves to.</summary>
    public static TheoryData<BackupItemStatus, WikiStatus> RepositoryItemsWithoutWiki => new()
    {
        { BackupItemStatus.Archived, WikiStatus.Off },
        { BackupItemStatus.UpToDate, WikiStatus.Off },
        { BackupItemStatus.Warning, WikiStatus.Off },
        { BackupItemStatus.WouldArchive, WikiStatus.Off },
        { BackupItemStatus.WouldClone, WikiStatus.Off },
        { BackupItemStatus.Skipped, WikiStatus.Unknown },
        { BackupItemStatus.Failed, WikiStatus.Unknown },
        { BackupItemStatus.Cancelled, WikiStatus.Unknown },
    };

    [Theory]
    [MemberData(nameof(WikiItems))]
    public void Resolve_WikiItem_ReturnsStatusOfWikiWhateverTheRepositoryItemIs(BackupItemStatus wiki, WikiStatus expected)
    {
        var run = new LatestRepositoryRun(Item(BackupTarget.Repository, BackupItemStatus.Failed), Item(BackupTarget.Wiki, wiki));

        WikiStatusResolver.Resolve(run).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(RepositoryItemsWithoutWiki))]
    public void Resolve_NoWikiItem_ReturnsStatusOfRepositoryItem(BackupItemStatus repository, WikiStatus expected)
    {
        var run = new LatestRepositoryRun(Item(BackupTarget.Repository, repository), null);

        WikiStatusResolver.Resolve(run).ShouldBe(expected);
    }

    [Fact]
    public void Resolve_NoRun_ReturnsUnknown() => WikiStatusResolver.Resolve(null).ShouldBe(WikiStatus.Unknown);

    /// <summary>
    /// Creates a history item of <c>octo/tool</c>.
    /// </summary>
    /// <param name="target">Whether the item is the repository or its wiki.</param>
    /// <param name="status">The outcome.</param>
    /// <returns>The item.</returns>
    private static RunHistoryItem Item(BackupTarget target, BackupItemStatus status) =>
        new("octo/tool", target, status, TimeSpan.Zero);
}
