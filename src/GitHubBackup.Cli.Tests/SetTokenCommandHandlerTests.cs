using GitHubBackup.Core;
using GitHubBackup.Core.Authentication;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Spectre.Console.Testing;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests <see cref="SetTokenCommandHandler"/> with a recording console and a substitute store.
/// </summary>
public sealed class SetTokenCommandHandlerTests : IDisposable
{
    private const string _token = "ghp_SetTokenValue0123456789abcdefABCDEF";
    private const string _location = "Windows Credential Manager (test-target)";

    private readonly TestConsole _output = TestCliConsole.CreateInteractiveOutput();
    private readonly StringWriter _error = new();
    private readonly ITokenStore _store = Substitute.For<ITokenStore>();

    /// <summary>
    /// Gives the store substitute its location.
    /// </summary>
    public SetTokenCommandHandlerTests() => _store.Location.Returns(_location);

    [Fact]
    public async Task RunAsync_TokenEntered_SavesTrimmedTokenAndReportsLocation()
    {
        _output.Input.PushTextWithEnter($"  {_token}  ");

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Success);
        await _store.Received(1).WriteAsync(_token, Arg.Any<CancellationToken>());
        _output.Output.ShouldContain(SetTokenCommandHandler.SavedMessage + _location);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_TokenEntered_NeverShowsToken()
    {
        _output.Input.PushTextWithEnter(_token);

        await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        _output.Output.ShouldContain(SetTokenCommandHandler.PromptText);
        _output.Output.ShouldNotContain(_token);
        _error.ToString().ShouldNotContain(_token);
    }

    [Fact]
    public async Task RunAsync_BlankInput_KeepsReadingUntilTokenEntered()
    {
        _output.Input.PushKey(ConsoleKey.Enter);
        _output.Input.PushTextWithEnter("   ");
        _output.Input.PushTextWithEnter(_token);

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Success);
        await _store.Received(1).WriteAsync(_token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_InputRedirected_ReturnsCriticalWithHintAndSavesNothing()
    {
        var exitCode = await CreateHandler(isInputRedirected: true).RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldBe(
            BackupSummaryRenderer.ErrorPrefix + SetTokenCommandHandler.RedirectedInputMessage + Environment.NewLine);
        await _store.DidNotReceiveWithAnyArgs().WriteAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_StoreFails_ReturnsCriticalWithOneErrorLineWithoutToken()
    {
        _output.Input.PushTextWithEnter(_token);
        _store.WriteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TokenStoreException("Cannot save the credential 'test-target': Access is denied."));

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldBe(
            BackupSummaryRenderer.ErrorPrefix + "Cannot save the credential 'test-target': Access is denied." + Environment.NewLine);
        _output.Output.ShouldNotContain(SetTokenCommandHandler.SavedMessage);
        _output.Output.ShouldNotContain(_token);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ThrowsAndSavesNothing()
    {
        _output.Input.PushTextWithEnter(_token);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => CreateHandler().RunAsync(cancellation.Token));

        await _store.DidNotReceiveWithAnyArgs().WriteAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Disposes the outputs.
    /// </summary>
    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    /// <summary>
    /// Creates the handler over the recording outputs and the store substitute.
    /// </summary>
    /// <param name="isInputRedirected">Whether the console reports redirected standard input.</param>
    /// <returns>The handler.</returns>
    private SetTokenCommandHandler CreateHandler(bool isInputRedirected = false) =>
        new(new CliConsole(_output, _error, isInputRedirected), _store);
}
