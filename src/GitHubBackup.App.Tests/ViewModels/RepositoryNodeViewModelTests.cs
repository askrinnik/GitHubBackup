using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Overview;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests the status icons of <see cref="RepositoryNodeViewModel"/> and <see cref="AccountNodeViewModel"/>: which
/// icons a node shows, the name of its status and whether its text is muted.
/// </summary>
public sealed class RepositoryNodeViewModelTests
{
    /// <summary>
    /// Gets the combinations of status and pause with the icons, the status name and the muting of the node: the
    /// status, whether the entry and its account are paused, then the check, the pause, the minus, the unavailable
    /// icon, the status name and whether the text is muted.
    /// </summary>
    public static TheoryData<RepositoryOverviewStatus, bool, bool, bool, bool, bool, bool, string?, bool> StatusCases { get; } = new()
    {
        { RepositoryOverviewStatus.Active, false, false, true, false, false, false, "Active", false },
        { RepositoryOverviewStatus.Active, true, false, false, true, false, false, "Paused", true },
        { RepositoryOverviewStatus.Unavailable, false, false, false, false, false, true, "Unavailable", false },
        { RepositoryOverviewStatus.Unavailable, true, false, false, true, false, true, "Unavailable, paused", true },
        { RepositoryOverviewStatus.Excluded, false, false, false, false, true, false, "Excluded", false },
        { RepositoryOverviewStatus.Excluded, true, false, false, false, true, false, "Excluded", true },
        { RepositoryOverviewStatus.Active, false, true, false, false, false, false, null, true },
        { RepositoryOverviewStatus.Active, true, true, false, false, false, false, null, true },
        { RepositoryOverviewStatus.Unavailable, true, true, false, false, false, true, "Unavailable", true },
        { RepositoryOverviewStatus.Excluded, false, true, false, false, true, false, "Excluded", true },
    };

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void StatusIcons_StatusAndPause_ShowExpectedIconsNameAndMuting(
        RepositoryOverviewStatus status,
        bool isPaused,
        bool isAccountPaused,
        bool active,
        bool paused,
        bool excluded,
        bool unavailable,
        string? statusName,
        bool muted)
    {
        var node = CreateNode(status, isPaused, isAccountPaused);

        node.ShowsActiveIcon.ShouldBe(active);
        node.ShowsPausedIcon.ShouldBe(paused);
        node.ShowsExcludedIcon.ShouldBe(excluded);
        node.ShowsUnavailableIcon.ShouldBe(unavailable);
        node.StatusName.ShouldBe(statusName);
        node.IsMuted.ShouldBe(muted);
    }

    [Theory]
    [InlineData(RepositoryOverviewStatus.Active, true)]
    [InlineData(RepositoryOverviewStatus.Unavailable, false)]
    [InlineData(RepositoryOverviewStatus.Excluded, false)]
    public void IsActive_PausedEntry_FollowsStatusOnly(RepositoryOverviewStatus status, bool expected)
    {
        var node = CreateNode(status, isPaused: true, isAccountPaused: true);

        node.IsActive.ShouldBe(expected);
    }

    [Theory]
    [InlineData(false, true, false, "Active", false)]
    [InlineData(true, false, true, "Paused", true)]
    public void AccountNode_Pause_ShowsCheckOrPause(bool isPaused, bool active, bool paused, string statusName, bool muted)
    {
        var node = new AccountNodeViewModel("octo", isPaused, [CreateNode(RepositoryOverviewStatus.Active, false, isPaused)], (_, _) => { });

        node.ShowsActiveIcon.ShouldBe(active);
        node.ShowsPausedIcon.ShouldBe(paused);
        node.ShowsExcludedIcon.ShouldBeFalse();
        node.ShowsUnavailableIcon.ShouldBeFalse();
        node.StatusName.ShouldBe(statusName);
        node.IsMuted.ShouldBe(muted);
    }

    [Fact]
    public void GroupNode_StandaloneRepositories_ShowsNoStatusIcon()
    {
        var node = new RepositoryGroupNodeViewModel([CreateNode(RepositoryOverviewStatus.Active, false, false)], (_, _) => { });

        node.ShowsActiveIcon.ShouldBeFalse();
        node.ShowsPausedIcon.ShouldBeFalse();
        node.StatusName.ShouldBeNull();
        node.IsMuted.ShouldBeFalse();
    }

    /// <summary>
    /// Creates the node of a repository of the account <c>octo</c>.
    /// </summary>
    /// <param name="status">The status of the repository.</param>
    /// <param name="isPaused">Whether the entry is paused.</param>
    /// <param name="isAccountPaused">Whether the account is paused.</param>
    /// <returns>The node.</returns>
    private static RepositoryNodeViewModel CreateNode(RepositoryOverviewStatus status, bool isPaused, bool isAccountPaused) =>
        new(
            new("octo", "tool", "octo", status, null, null)
            {
                RepositoryId = 1,
                ArchiveFolder = @"C:\backup\octo\tool",
                IsPaused = isPaused,
                IsAccountPaused = isAccountPaused,
            },
            (_, _) => { });
}
