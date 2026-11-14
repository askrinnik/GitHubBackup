using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Discovery;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using NSubstitute;

namespace GitHubBackup.App.Tests.Discovery;

/// <summary>
/// Tests <see cref="DialogDiscoveryDecisionHandler"/>.
/// </summary>
public sealed class DialogDiscoveryDecisionHandlerTests
{
    private static readonly GitHubRepository _tool =
        new(10, "octo", "tool", false, false, false, true, "main", "https://github.com/octo/tool");

    private static readonly GitHubRepository _lib =
        new(20, "acme", "lib", false, false, false, true, "main", "https://github.com/acme/lib");

    private static readonly NewRepositoryProposal _toolProposal = new(_tool, "octo", @"C:\backup\octo\tool");

    private static readonly NewRepositoryProposal _libProposal = new(_lib, null, @"C:\backup\acme\lib");

    private static readonly RenameProposal _renameProposal = new(
        new(new("octo", "app", 1, RepositoryStatus.Active, "octo"),
            new(1, "octo", "app2", false, false, false, true, "main", "https://github.com/octo/app2")),
        @"C:\backup\octo\app",
        @"C:\backup\octo\app2");

    private static readonly TrackedRepository _missing = new("octo", "gone", 3, RepositoryStatus.Active, "octo");

    private readonly IDiscoveryDialogService _dialogService = Substitute.For<IDiscoveryDialogService>();

    private readonly DialogDiscoveryDecisionHandler _handler;

    private int _cancelRunCalls;

    /// <summary>
    /// Creates the handler over the substitute dialog service, counting the calls of the cancellation callback.
    /// </summary>
    public DialogDiscoveryDecisionHandlerTests() => _handler = new(_dialogService, () => _cancelRunCalls++);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReviewAsync_Findings_ShowsDialogWithEveryRow()
    {
        DiscoveryReviewViewModel? shown = null;
        _dialogService.ShowAsync(Arg.Do<DiscoveryReviewViewModel>(viewModel => shown = viewModel), Token).Returns(true);

        await _handler.ReviewAsync(new([_toolProposal], [_renameProposal], [_missing], []), Token);

        shown.ShouldNotBeNull().Rows.Select(row => row.Repository).ShouldBe(["octo/tool", "octo/app2", "octo/gone"]);
    }

    [Fact]
    public async Task ReviewAsync_NoFindings_ShowsNoDialog()
    {
        await _handler.ReviewAsync(new([], [], [], []), Token);

        await _dialogService.DidNotReceiveWithAnyArgs().ShowAsync(default!, Token);
    }

    [Fact]
    public async Task ReviewAsync_OnlyMissing_ShowsDialog()
    {
        _dialogService.ShowAsync(default!, Token).ReturnsForAnyArgs(true);

        await _handler.ReviewAsync(new([], [], [_missing], []), Token);

        await _dialogService.ReceivedWithAnyArgs(1).ShowAsync(default!, Token);
        _cancelRunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Decide_AfterApply_ReturnsDecisionsChosenInDialog()
    {
        _dialogService.ShowAsync(default!, Token).ReturnsForAnyArgs(call =>
        {
            var viewModel = call.Arg<DiscoveryReviewViewModel>();
            viewModel.NewRows[0].Branch = "develop";
            viewModel.NewRows[1].Choice = NewRepositoryChoice.ExcludeGlobally;
            viewModel.RenamedRows[0].Variant = RenameDecision.RenameArchiveFolder;
            return true;
        });

        await _handler.ReviewAsync(new([_toolProposal, _libProposal], [_renameProposal], [], []), Token);

        (await _handler.DecideNewAsync(_toolProposal, Token)).ShouldBe(NewRepositoryDecision.Add("develop"));
        (await _handler.DecideNewAsync(_libProposal, Token)).ShouldBe(NewRepositoryDecision.Exclude(ExclusionScope.Global));
        (await _handler.DecideRenameAsync(_renameProposal, Token)).ShouldBe(RenameDecision.RenameArchiveFolder);
    }

    [Fact]
    public async Task Decide_AfterApplyWithDefaults_AddsNewAndKeepsArchiveFolder()
    {
        _dialogService.ShowAsync(default!, Token).ReturnsForAnyArgs(true);

        await _handler.ReviewAsync(new([_toolProposal], [_renameProposal], [], []), Token);

        (await _handler.DecideNewAsync(_toolProposal, Token)).ShouldBe(NewRepositoryDecision.Add());
        (await _handler.DecideRenameAsync(_renameProposal, Token)).ShouldBe(RenameDecision.KeepArchiveFolder);
    }

    [Fact]
    public async Task Decide_RepositoryNotReviewed_SkipsNewAndKeepsArchiveFolder()
    {
        (await _handler.DecideNewAsync(_toolProposal, Token)).ShouldBe(NewRepositoryDecision.Skip);
        (await _handler.DecideRenameAsync(_renameProposal, Token)).ShouldBe(RenameDecision.KeepArchiveFolder);
    }

    [Fact]
    public async Task ReviewAsync_DialogCancelled_CancelsRunAndThrows()
    {
        _dialogService.ShowAsync(default!, Token).ReturnsForAnyArgs(false);

        await Should.ThrowAsync<OperationCanceledException>(
            () => _handler.ReviewAsync(new([_toolProposal], [], [], []), Token));

        _cancelRunCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ReviewAsync_TokenCancelledWhileDialogOpen_CancelsRunAndThrows()
    {
        using var cancellation = new CancellationTokenSource();
        _dialogService.ShowAsync(default!, Token).ReturnsForAnyArgs(async _ =>
        {
            await cancellation.CancelAsync();
            return false;
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => _handler.ReviewAsync(new([_toolProposal], [], [], []), cancellation.Token));

        _cancelRunCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ReviewAsync_TokenCancelledBeforeReview_ThrowsWithoutDialog()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _handler.ReviewAsync(new([_toolProposal], [], [], []), cancellation.Token));

        await _dialogService.DidNotReceiveWithAnyArgs().ShowAsync(default!, Token);
    }

    [Fact]
    public async Task ConfirmIdSaveAsync_ReturnsTrue()
    {
        var save = await _handler.ConfirmIdSaveAsync(new("octo/tool", 7), Token);

        save.ShouldBeTrue();
        await _dialogService.DidNotReceiveWithAnyArgs().ShowAsync(default!, Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Decide_CancelledToken_ReturnsCanceledTask(int question)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Task task = question switch
        {
            0 => _handler.DecideNewAsync(_toolProposal, cancellation.Token),
            1 => _handler.DecideRenameAsync(_renameProposal, cancellation.Token),
            _ => _handler.ConfirmIdSaveAsync(new("octo/tool", 7), cancellation.Token),
        };

        task.IsCanceled.ShouldBeTrue();
        await Should.ThrowAsync<OperationCanceledException>(() => task);
    }
}
