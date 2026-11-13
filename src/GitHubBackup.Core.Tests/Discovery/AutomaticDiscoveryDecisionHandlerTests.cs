using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Tests.Discovery;

/// <summary>
/// Tests <see cref="AutomaticDiscoveryDecisionHandler"/>.
/// </summary>
public sealed class AutomaticDiscoveryDecisionHandlerTests
{
    private static readonly GitHubRepository _repository =
        new(1, "octo", "tool", false, false, false, true, "main", "https://github.com/octo/tool");

    private readonly AutomaticDiscoveryDecisionHandler _handler = new();

    [Theory]
    [InlineData("octo")]
    [InlineData(null)]
    public async Task DecideNewAsync_AnyRepository_AddsWithDefaultBranchAndFolder(string? accountLogin)
    {
        var decision = await _handler.DecideNewAsync(
            new(_repository, accountLogin, @"C:\backup\octo\tool"), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Add());
    }

    [Fact]
    public async Task DecideRenameAsync_AnyRepository_KeepsArchiveFolder()
    {
        var renamed = new RenamedRepository(new("octo", "old", 1, RepositoryStatus.Active, "octo"), _repository);

        var decision = await _handler.DecideRenameAsync(
            new(renamed, @"C:\backup\octo\old", @"C:\backup\octo\tool"), TestContext.Current.CancellationToken);

        decision.ShouldBe(RenameDecision.KeepArchiveFolder);
    }

    [Fact]
    public async Task ReviewAsync_AnyReview_Completes()
    {
        var task = _handler.ReviewAsync(
            new([new(_repository, "octo", @"C:\backup\octo\tool")], [], [], []), TestContext.Current.CancellationToken);

        await task;

        task.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task ReviewAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _handler.ReviewAsync(new([], [], [], []), cancellation.Token));
    }

    [Fact]
    public async Task ConfirmIdSaveAsync_AnyId_ReturnsTrue()
    {
        var save = await _handler.ConfirmIdSaveAsync(new("octo/tool", 7), TestContext.Current.CancellationToken);

        save.ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmIdSaveAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _handler.ConfirmIdSaveAsync(new("octo/tool", 7), cancellation.Token));
    }

    [Fact]
    public async Task DecideNewAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _handler.DecideNewAsync(new(_repository, null, @"C:\backup\octo\tool"), cancellation.Token));
    }

    [Fact]
    public async Task DecideRenameAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var renamed = new RenamedRepository(new("octo", "old", 1, RepositoryStatus.Active, "octo"), _repository);

        await Should.ThrowAsync<OperationCanceledException>(
            () => _handler.DecideRenameAsync(new(renamed, @"C:\a", @"C:\b"), cancellation.Token));
    }
}
