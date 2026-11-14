using GitHubBackup.App.Shell;
using GitHubBackup.Core.Processes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.App.Tests.Shell;

/// <summary>
/// Tests <see cref="ExplorerLauncher"/> over a substituted process runner.
/// </summary>
public sealed class ExplorerLauncherTests
{
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();

    private readonly ExplorerLauncher _launcher;

    private ProcessRequest? _request;

    /// <summary>
    /// Creates the launcher over a runner that records the request and reports the exit code Explorer returns on
    /// success.
    /// </summary>
    public ExplorerLauncherTests()
    {
        _processRunner.RunAsync(Arg.Do<ProcessRequest>(request => _request = request), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, TimeSpan.Zero, [], []));
        _launcher = new(_processRunner);
    }

    [Fact]
    public async Task OpenFolderAsync_Folder_StartsExplorerWithFolderAsOnlyArgument()
    {
        await _launcher.OpenFolderAsync(@"C:\backup\octo\my tool", TestContext.Current.CancellationToken);

        _request.ShouldNotBeNull();
        _request.Arguments.ShouldBe([@"C:\backup\octo\my tool"]);
        AssertExplorer(_request);
    }

    [Fact]
    public async Task ShowFileAsync_File_StartsExplorerWithSelectArgument()
    {
        await _launcher.ShowFileAsync(@"C:\backup\octo\tool\tool 1.zip", TestContext.Current.CancellationToken);

        _request.ShouldNotBeNull();
        _request.Arguments.ShouldBe([@"/select,C:\backup\octo\tool\tool 1.zip"]);
        AssertExplorer(_request);
    }

    [Fact]
    public async Task ShowFileAsync_FileNameWithComma_OpensContainingFolder()
    {
        await _launcher.ShowFileAsync(@"C:\backup\octo\tool\x.zip,notepad.exe", TestContext.Current.CancellationToken);

        _request.ShouldNotBeNull();
        _request.Arguments.ShouldBe([@"C:\backup\octo\tool"]);
        AssertExplorer(_request);
    }

    [Fact]
    public async Task OpenFolderAsync_FolderWithComma_ThrowsArgumentExceptionAndStartsNothing()
    {
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => _launcher.OpenFolderAsync(@"C:\backup\a,b", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(@"C:\backup\a,b");
        await _processRunner.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowFileAsync_FolderWithComma_ThrowsArgumentExceptionAndStartsNothing()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => _launcher.ShowFileAsync(@"C:\backup\a,b\x.zip", TestContext.Current.CancellationToken));

        await _processRunner.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenFolderAsync_ProcessCannotStart_PropagatesProcessStartException()
    {
        var error = new ProcessStartException("explorer.exe cannot be started.");
        _processRunner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(error);

        var exception = await Should.ThrowAsync<ProcessStartException>(
            () => _launcher.OpenFolderAsync(@"C:\backup", TestContext.Current.CancellationToken));

        exception.ShouldBeSameAs(error);
    }

    /// <summary>
    /// Checks that a request starts <c>explorer.exe</c> of the Windows folder in that folder.
    /// </summary>
    /// <param name="request">The request.</param>
    private static void AssertExplorer(ProcessRequest request)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        request.FileName.ShouldBe(Path.Combine(windows, "explorer.exe"));
        request.WorkingDirectory.ShouldBe(windows);
        request.EnvironmentVariables.ShouldBeEmpty();
    }
}
