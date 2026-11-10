using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Tests.BackupConfiguration;
using GitHubBackup.Infrastructure.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Tools;

/// <summary>
/// Tests <see cref="ToolLocator"/> with a fake file system, a fake process runner and a fake <c>PATH</c>.
/// </summary>
public sealed class ToolLocatorTests
{
    private const string _gitPath = @"C:\Tools\Git\cmd\git.exe";
    private const string _sevenZipPath = @"D:\Apps\7z\7z.exe";
    private const string _defaultSevenZip = ToolsOptions.DefaultSevenZipPath;

    private readonly MockFileSystem _fileSystem = new();
    private readonly RecordingLogger<ToolLocator> _logger = new();
    private readonly List<ProcessRequest> _requests = [];
    private readonly ToolsOptions _options = new() { GitPath = _gitPath, SevenZipPath = _sevenZipPath };
    private string? _pathVariable;
    private Func<ProcessRequest, ProcessResult> _respond;

    /// <summary>
    /// Creates the fixture with healthy tools at the configured paths.
    /// </summary>
    public ToolLocatorTests()
    {
        _respond = Healthy;
        _fileSystem.AddFile(_gitPath, new MockFileData(string.Empty));
        _fileSystem.AddFile(_sevenZipPath, new MockFileData(string.Empty));
    }

    [Fact]
    public async Task LocateAsync_AllToolsPresent_ReturnsPathsAndVersions()
    {
        var tools = await CreateLocator().LocateAsync(TestContext.Current.CancellationToken);

        tools.ShouldBe(new ToolSet(
            new LocatedTool(_gitPath, "2.46.0.windows.1"), "3.5.1", new LocatedTool(_sevenZipPath, "24.08")));
    }

    [Fact]
    public async Task LocateAsync_AllToolsPresent_LogsEachVersion()
    {
        await CreateLocator().LocateAsync(TestContext.Current.CancellationToken);

        _logger.Entries.ShouldBe(
        [
            (LogLevel.Information, $"git 2.46.0.windows.1 found at {_gitPath}"),
            (LogLevel.Information, $"Git LFS 3.5.1 found at {_gitPath}"),
            (LogLevel.Information, $"7-Zip 24.08 found at {_sevenZipPath}"),
        ]);
    }

    [Fact]
    public async Task LocateAsync_GitPathConfiguredButMissing_ThrowsNamingKeyAndPath()
    {
        _options.GitPath = @"C:\Missing\git.exe";

        var exception = await Should.ThrowAsync<ToolUnavailableException>(
            () => CreateLocator().LocateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Tools:GitPath");
        exception.Message.ShouldContain(@"C:\Missing\git.exe");
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task LocateAsync_SevenZipPathConfiguredButMissing_ThrowsNamingKeyAndPath()
    {
        _options.SevenZipPath = @"C:\Missing\7z.exe";

        var exception = await Should.ThrowAsync<ToolUnavailableException>(
            () => CreateLocator().LocateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Tools:SevenZipPath");
        exception.Message.ShouldContain(@"C:\Missing\7z.exe");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task LocateAsync_GitPathNotSet_FindsGitInPath(string? configured)
    {
        _options.GitPath = configured;
        _fileSystem.AddFile(@"C:\Program Files\Git\cmd\git.exe", new MockFileData(string.Empty));
        _pathVariable = @"C:\Windows;C:\Program Files\Git\cmd";

        var tools = await CreateLocator().LocateAsync(TestContext.Current.CancellationToken);

        tools.Git.Path.ShouldBe(@"C:\Program Files\Git\cmd\git.exe");
    }

    [Fact]
    public async Task LocateAsync_PathHasQuotedEntry_FindsGit()
    {
        _options.GitPath = null;
        _fileSystem.AddFile(@"C:\Program Files\Git\cmd\git.exe", new MockFileData(string.Empty));
        _pathVariable = "\"C:\\Program Files\\Git\\cmd\"";

        var tools = await CreateLocator().LocateAsync(TestContext.Current.CancellationToken);

        tools.Git.Path.ShouldBe(@"C:\Program Files\Git\cmd\git.exe");
    }

    [Fact]
    public async Task LocateAsync_PathHasRelativeOrEmptyEntries_SkipsThem()
    {
        _options.GitPath = null;
        _fileSystem.AddFile(@"C:\Work\git.exe", new MockFileData(string.Empty));
        _fileSystem.AddFile(@"C:\Work\bin\git.exe", new MockFileData(string.Empty));
        _fileSystem.AddFile(@"C:\Real\git.exe", new MockFileData(string.Empty));
        _fileSystem.Directory.SetCurrentDirectory(@"C:\Work");
        _pathVariable = @";  ;.;bin;C:\Real";

        var tools = await CreateLocator().LocateAsync(TestContext.Current.CancellationToken);

        tools.Git.Path.ShouldBe(@"C:\Real\git.exe");
    }

    [Fact]
    public async Task LocateAsync_SeveralPathEntriesHoldGit_UsesFirst()
    {
        _options.GitPath = null;
        _fileSystem.AddFile(@"C:\First\git.exe", new MockFileData(string.Empty));
        _fileSystem.AddFile(@"C:\Second\git.exe", new MockFileData(string.Empty));
        _pathVariable = @"C:\Empty;C:\First;C:\Second";

        var tools = await CreateLocator().LocateAsync(TestContext.Current.CancellationToken);

        tools.Git.Path.ShouldBe(@"C:\First\git.exe");
    }

    [Theory]
    [InlineData(@"C:\Windows;C:\Other")]
    [InlineData(null)]
    public async Task LocateAsync_GitNotFoundInPath_ThrowsSuggestingGitPath(string? pathVariable)
    {
        _options.GitPath = null;
        _pathVariable = pathVariable;

        var exception = await Should.ThrowAsync<ToolUnavailableException>(
            () => CreateLocator().LocateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("PATH");
        exception.Message.ShouldContain("Tools:GitPath");
    }

    [Fact]
    public async Task LocateAsync_GitLfsMissing_ThrowsNamingGitLfs()
    {
        _respond = request => request.Arguments.SequenceEqual(["lfs", "version"])
            ? Result(1, "git: 'lfs' is not a git command.")
            : Healthy(request);

        var exception = await Should.ThrowAsync<ToolUnavailableException>(
            () => CreateLocator().LocateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Git LFS");
        exception.Message.ShouldContain("code 1");
    }

    [Theory]
    [InlineData(0, "git", _gitPath)]
    [InlineData(1, "Git LFS", _gitPath)]
    [InlineData(2, "7-Zip", _sevenZipPath)]
    public async Task LocateAsync_ToolExitsNonZero_ThrowsWithExitCode(int failing, string tool, string path)
    {
        _respond = request => IndexOf(request) == failing ? Result(3) : Healthy(request);

        var exception = await Should.ThrowAsync<ToolUnavailableException>(
            () => CreateLocator().LocateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(tool);
        exception.Message.ShouldContain(path);
        exception.Message.ShouldContain("code 3");
    }

    [Theory]
    [InlineData(0, "git", _gitPath)]
    [InlineData(1, "Git LFS", _gitPath)]
    [InlineData(2, "7-Zip", _sevenZipPath)]
    public async Task LocateAsync_VersionNotRecognized_Throws(int failing, string tool, string path)
    {
        _respond = request => IndexOf(request) == failing ? Result(0, "Microsoft Windows [Version 10.0]") : Healthy(request);

        var exception = await Should.ThrowAsync<ToolUnavailableException>(
            () => CreateLocator().LocateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(tool);
        exception.Message.ShouldContain(path);
    }

    [Fact]
    public async Task LocateAsync_ProcessStartFails_WrapsInToolUnavailable()
    {
        var inner = new ProcessStartException("access denied");
        _respond = _ => throw inner;

        var exception = await Should.ThrowAsync<ToolUnavailableException>(
            () => CreateLocator().LocateAsync(TestContext.Current.CancellationToken));

        exception.InnerException.ShouldBeSameAs(inner);
        exception.Message.ShouldContain(_gitPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task LocateAsync_SevenZipPathEmpty_UsesDefaultPath(string? configured)
    {
        _options.SevenZipPath = configured;
        _fileSystem.AddFile(_defaultSevenZip, new MockFileData(string.Empty));

        var tools = await CreateLocator().LocateAsync(TestContext.Current.CancellationToken);

        tools.SevenZip.Path.ShouldBe(_defaultSevenZip);
    }

    [Fact]
    public async Task LocateAsync_Cancelled_ThrowsOperationCanceled()
    {
        using var source = new CancellationTokenSource();
        _respond = _ => throw new OperationCanceledException(source.Token);

        await Should.ThrowAsync<OperationCanceledException>(() => CreateLocator().LocateAsync(source.Token));
    }

    [Fact]
    public async Task LocateAsync_AllToolsPresent_RunsEachToolInItsOwnFolder()
    {
        await CreateLocator().LocateAsync(TestContext.Current.CancellationToken);

        _requests.Select(request => (request.FileName, request.WorkingDirectory, string.Join(' ', request.Arguments))).ShouldBe(
        [
            (_gitPath, @"C:\Tools\Git\cmd", "version"),
            (_gitPath, @"C:\Tools\Git\cmd", "lfs version"),
            (_sevenZipPath, @"D:\Apps\7z", string.Empty),
        ]);
    }

    /// <summary>
    /// Creates the locator over the fixture state at the time of the call.
    /// </summary>
    /// <returns>The locator under test.</returns>
    private ToolLocator CreateLocator()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<ProcessRequest>();
            _requests.Add(request);
            return Task.FromResult(_respond(request));
        });

        return new ToolLocator(Options.Create(_options), runner, _fileSystem, _logger)
        {
            GetEnvironmentVariable = name => name == "PATH" ? _pathVariable : null,
        };
    }

    /// <summary>
    /// Returns the position of the check a request belongs to: 0 for git, 1 for git-lfs, 2 for 7-Zip.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The position.</returns>
    private static int IndexOf(ProcessRequest request) => request.Arguments.Count switch
    {
        0 => 2,
        1 => 0,
        _ => 1,
    };

    /// <summary>
    /// Answers each check the way the real tool does.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The result.</returns>
    private static ProcessResult Healthy(ProcessRequest request) => IndexOf(request) switch
    {
        0 => Result(0, "git version 2.46.0.windows.1"),
        1 => Result(0, "git-lfs/3.5.1 (GitHub; windows amd64; go 1.22.5)"),
        _ => Result(0, string.Empty, "7-Zip 24.08 (x64) : Copyright (c) 1999-2024 Igor Pavlov : 2024-08-11"),
    };

    /// <summary>
    /// Builds a process result.
    /// </summary>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="output">The standard output lines.</param>
    /// <returns>The result.</returns>
    private static ProcessResult Result(int exitCode, params string[] output) =>
        new(exitCode, TimeSpan.Zero, output, []);
}
