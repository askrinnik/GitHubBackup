using GitHubBackup.Core;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.Telemetry;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Spectre.Console.Testing;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests <see cref="SetOtlpHeadersCommandHandler"/> with a recording console and substitute stores.
/// </summary>
public sealed class SetOtlpHeadersCommandHandlerTests : IDisposable
{
    private const string _headers = "api-key=set-headers-secret";
    private const string _location = "Windows Credential Manager (test-target)";

    private readonly TestConsole _output = TestCliConsole.CreateInteractiveOutput();
    private readonly StringWriter _error = new();
    private readonly IOtlpHeadersStore _store = Substitute.For<IOtlpHeadersStore>();
    private readonly ITelemetrySettingsStore _telemetrySettings = Substitute.For<ITelemetrySettingsStore>();
    private readonly SecretMasker _masker = new();

    /// <summary>
    /// Gives the store substitute its location.
    /// </summary>
    public SetOtlpHeadersCommandHandlerTests() => _store.Location.Returns(_location);

    [Fact]
    public async Task RunAsync_HeadersEntered_SavesTrimmedHeadersAndReportsLocation()
    {
        _output.Input.PushTextWithEnter($"  {_headers}  ");

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Success);
        await _store.Received(1).WriteAsync(_headers, Arg.Any<CancellationToken>());
        _output.Output.ShouldContain(SetOtlpHeadersCommandHandler.SavedMessage + _location);
        _output.Output.ShouldNotContain(SetOtlpHeadersCommandHandler.AppSettingsHeadersMessage);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_HeadersEntered_NeverShowsThemAndRegistersThemWithMasker()
    {
        _output.Input.PushTextWithEnter(_headers);

        await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        _output.Output.ShouldContain(SetOtlpHeadersCommandHandler.PromptText);
        _output.Output.ShouldNotContain("set-headers-secret");
        _masker.Mask("Sent set-headers-secret.").ShouldBe("Sent ***.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunAsync_EmptyInput_DeletesHeaders(string input)
    {
        _output.Input.PushTextWithEnter(input);

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Success);
        await _store.Received(1).DeleteAsync(Arg.Any<CancellationToken>());
        await _store.DidNotReceive().WriteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _output.Output.ShouldContain(SetOtlpHeadersCommandHandler.RemovedMessage + _location);
    }

    [Theory]
    [InlineData("api-key set-headers-secret")]
    [InlineData("api-key=set-headers-secret,,x=y")]
    public async Task RunAsync_MalformedHeaders_ReturnsCriticalNamingKeyWithoutValue(string input)
    {
        _output.Input.PushTextWithEnter(input);

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        var error = _error.ToString();
        error.ShouldStartWith(BackupSummaryRenderer.ErrorPrefix + TelemetrySettings.HeadersKey);
        error.ShouldNotContain("set-headers-secret");
        _store.ReceivedCalls().Where(call => call.GetMethodInfo().Name != "get_Location").ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_InputRedirected_ReturnsCriticalWithHintAndSavesNothing()
    {
        var exitCode = await CreateHandler(isInputRedirected: true).RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldBe(
            BackupSummaryRenderer.ErrorPrefix + SetOtlpHeadersCommandHandler.RedirectedInputMessage + Environment.NewLine);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_StoreFails_ReturnsCriticalWithOneErrorLineWithoutValue()
    {
        _output.Input.PushTextWithEnter(_headers);
        _store.WriteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OtlpHeadersStoreException("Cannot save the credential 'test-target': Access is denied."));

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldBe(
            BackupSummaryRenderer.ErrorPrefix + "Cannot save the credential 'test-target': Access is denied." + Environment.NewLine);
        _output.Output.ShouldNotContain(SetOtlpHeadersCommandHandler.SavedMessage);
        _output.Output.ShouldNotContain("set-headers-secret");
    }

    [Fact]
    public async Task RunAsync_HeadersInAppSettings_WarnsAfterSaving()
    {
        _telemetrySettings.HeadersInAppSettings.Returns(true);
        _output.Input.PushTextWithEnter(_headers);

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Success);
        _output.Output.ShouldContain(BackupSummaryRenderer.WarningPrefix + SetOtlpHeadersCommandHandler.AppSettingsHeadersMessage);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ThrowsAndChangesNothing()
    {
        _output.Input.PushTextWithEnter(_headers);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => CreateHandler().RunAsync(cancellation.Token));

        _store.ReceivedCalls().ShouldBeEmpty();
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
    /// Creates the handler over the recording outputs and the substitutes.
    /// </summary>
    /// <param name="isInputRedirected">Whether the console reports redirected standard input.</param>
    /// <returns>The handler.</returns>
    private SetOtlpHeadersCommandHandler CreateHandler(bool isInputRedirected = false) =>
        new(new CliConsole(_output, _error, isInputRedirected), _store, _telemetrySettings, _masker);
}
