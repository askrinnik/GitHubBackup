using GitHubBackup.App.Discovery;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.App.Tests.Discovery;

/// <summary>
/// Tests <see cref="DeferredDiscoveryDecisionHandler"/>.
/// </summary>
public sealed class DeferredDiscoveryDecisionHandlerTests
{
    private static readonly GitHubRepository _repository =
        new(1, "octo", "tool", false, false, false, true, "main", "https://github.com/octo/tool");

    private static readonly RenameProposal _renameProposal = new(
        new(new("octo", "old", 1, RepositoryStatus.Active, "octo"), _repository),
        @"C:\backup\octo\old",
        @"C:\backup\octo\tool");

    private readonly DeferredDiscoveryDecisionHandler _handler = new();

    [Fact]
    public async Task DecideNewAsync_ReturnsSkip()
    {
        var decision = await _handler.DecideNewAsync(
            new(_repository, "octo", @"C:\backup\octo\tool"), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Skip);
    }

    [Fact]
    public async Task DecideRenameAsync_ReturnsKeepArchiveFolder()
    {
        var decision = await _handler.DecideRenameAsync(_renameProposal, TestContext.Current.CancellationToken);

        decision.ShouldBe(RenameDecision.KeepArchiveFolder);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Decide_CancelledToken_ReturnsCanceledTask(bool isNewRepository)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Task task = isNewRepository
            ? _handler.DecideNewAsync(new(_repository, null, @"C:\backup\octo\tool"), cancellation.Token)
            : _handler.DecideRenameAsync(_renameProposal, cancellation.Token);

        task.IsCanceled.ShouldBeTrue();
        await Should.ThrowAsync<OperationCanceledException>(() => task);
    }
}
