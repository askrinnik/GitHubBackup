using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Processes;
using GitHubBackup.Infrastructure.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Infrastructure.Tests.Authentication;

/// <summary>
/// Tests <see cref="GitHubCliTokenSource"/> with a fake file system, a substitute process runner and a fake
/// <c>PATH</c>.
/// </summary>
public sealed class GitHubCliTokenSourceTests
{
    private const string _ghFolder = @"C:\Program Files\GitHub CLI";
    private const string _ghPath = @"C:\Program Files\GitHub CLI\gh.exe";
    private const string _token = "gho_CliTokenValue0123456789abcdefABCDEF";

    private const int _notFoundEventId = 2;
    private const int _startFailedEventId = 3;
    private const int _noTokenEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();
    private readonly FakeLogger<GitHubCliTokenSource> _logger = new();
    private string? _pathVariable = $@"C:\Windows\System32;{_ghFolder}";

    /// <summary>
    /// Creates the fixture with <c>gh.exe</c> installed in a folder of <c>PATH</c>.
    /// </summary>
    public GitHubCliTokenSourceTests() => _fileSystem.AddFile(_ghPath, new MockFileData(string.Empty));

    [Fact]
    public void Name_Always_NamesCommand() => CreateSource().Name.ShouldBe("GitHub CLI (gh auth token)");

    [Fact]
    public async Task GetTokenAsync_LoggedIn_ReturnsFirstNonBlankLineTrimmed()
    {
        Respond(new ProcessResult(0, TimeSpan.FromSeconds(1), ["", $"  {_token}  ", "other"], []));

        var token = await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBe(_token);
    }

    [Fact]
    public async Task GetTokenAsync_LoggedIn_RunsAuthTokenWithSecretOutputAndNoPrompts()
    {
        ProcessRequest? request = null;
        _processRunner.RunAsync(Arg.Do<ProcessRequest>(value => request = value), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, TimeSpan.FromSeconds(1), [_token], []));

        await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        request.ShouldNotBeNull();
        request.FileName.ShouldBe(_ghPath);
        request.Arguments.ShouldBe(["auth", "token", "--hostname", "github.com"]);
        request.WorkingDirectory.ShouldBe(_ghFolder);
        request.IsStandardOutputSecret.ShouldBeTrue();
        request.EnvironmentVariables.ShouldBe(
        [
            new ProcessEnvironmentVariable("GH_PROMPT_DISABLED", "1"),
            new ProcessEnvironmentVariable("GH_NO_UPDATE_NOTIFIER", "1"),
        ]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Windows\System32")]
    public async Task GetTokenAsync_GhNotInPath_ReturnsNullWithoutRunning(string? pathVariable)
    {
        _pathVariable = pathVariable;

        var token = await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBeNull();
        await _processRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, TestContext.Current.CancellationToken);
        _logger.Collector.GetSnapshot().ShouldHaveSingleItem().Id.Id.ShouldBe(_notFoundEventId);
    }

    [Theory]
    [InlineData(";;")]
    [InlineData(@"GitHub CLI;.\GitHub CLI")]
    public async Task GetTokenAsync_OnlyBlankOrRelativeEntries_ReturnsNullWithoutRunning(string pathVariable)
    {
        _fileSystem.Directory.SetCurrentDirectory(@"C:\Program Files");
        _pathVariable = pathVariable;

        var token = await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBeNull();
        await _processRunner.DidNotReceiveWithAnyArgs().RunAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetTokenAsync_StartFails_LogsWarningAndReturnsNull()
    {
        _processRunner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProcessStartException("Access is denied."));

        var token = await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBeNull();
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_startFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_ghPath);
    }

    [Fact]
    public async Task GetTokenAsync_NonZeroExitCode_ReturnsNullEvenWithOutput()
    {
        Respond(new ProcessResult(1, TimeSpan.FromSeconds(1), [_token], ["no oauth token found for github.com"]));

        var token = await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBeNull();
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_noTokenEventId);
        record.GetStructuredStateValue("ExitCode").ShouldBe("1");
    }

    [Theory]
    [InlineData]
    [InlineData("", "   ")]
    public async Task GetTokenAsync_BlankOutput_ReturnsNull(params string[] output)
    {
        Respond(new ProcessResult(0, TimeSpan.FromSeconds(1), output, []));

        var token = await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBeNull();
        _logger.Collector.GetSnapshot().ShouldHaveSingleItem().Id.Id.ShouldBe(_noTokenEventId);
    }

    [Fact]
    public async Task GetTokenAsync_Cancelled_Throws()
    {
        _processRunner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateSource().GetTokenAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task GetTokenAsync_AnyOutcome_NeverLogsToken(int exitCode)
    {
        Respond(new ProcessResult(exitCode, TimeSpan.FromSeconds(1), [_token], [_token]));

        await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        foreach (var record in _logger.Collector.GetSnapshot())
        {
            record.Message.ShouldNotContain(_token);
            record.StructuredState.ShouldNotBeNull()
                .ShouldAllBe(pair => pair.Value == null || !pair.Value.Contains(_token));
        }
    }

    /// <summary>
    /// Makes the process runner return <paramref name="result"/> for every request.
    /// </summary>
    /// <param name="result">The result of the run.</param>
    private void Respond(ProcessResult result) =>
        _processRunner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(result);

    /// <summary>
    /// Creates the source over the fakes, reading <c>PATH</c> from <see cref="_pathVariable"/>.
    /// </summary>
    /// <returns>The source.</returns>
    private GitHubCliTokenSource CreateSource() =>
        new(_processRunner, _fileSystem, _logger) { GetEnvironmentVariable = _ => _pathVariable };
}
