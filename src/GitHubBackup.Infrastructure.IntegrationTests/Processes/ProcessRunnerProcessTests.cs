using System.Diagnostics;
using System.IO.Abstractions;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitHubBackup.Infrastructure.IntegrationTests.Processes;

/// <summary>
/// Tests <see cref="ProcessRunner"/> against real <c>cmd.exe</c> processes in a temporary folder.
/// </summary>
public sealed class ProcessRunnerProcessTests : IDisposable
{
    private readonly TemporaryContentRoot _folder = new();
    private readonly ProcessRunner _runner =
        new(new SecretMasker(), new FileSystem(), TimeProvider.System, NullLogger<ProcessRunner>.Instance);

    [Fact]
    public async Task RunAsync_ExitCodeZero_ReturnsSucceededWithOutputLines()
    {
        var request = TestScript.Create(_folder.FolderPath, ["echo first", "echo second"]);

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0);
        result.Succeeded.ShouldBeTrue();
        result.StandardOutput.ShouldBe(["first", "second"]);
        result.StandardError.ShouldBeEmpty();
        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_ReturnsExitCodeWithoutThrowing()
    {
        var request = TestScript.Create(_folder.FolderPath, ["echo failing", "exit /b 3"]);

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(3);
        result.Succeeded.ShouldBeFalse();
        result.StandardOutput.ShouldBe(["failing"]);
    }

    [Fact]
    public async Task RunAsync_StderrWithExitCodeZero_IsSucceeded()
    {
        var request = TestScript.Create(_folder.FolderPath, [">&2 echo progress", "exit /b 0"]);

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        result.StandardError.ShouldBe(["progress"]);
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_NoOutput_ReturnsEmptyLines()
    {
        var request = TestScript.Create(_folder.FolderPath, ["exit /b 0"]);

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.StandardOutput.ShouldBeEmpty();
        result.StandardError.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_ArgumentsWithSpaces_ReachProcessAsSeparateArguments()
    {
        var request = TestScript.Create(
            _folder.FolderPath, ["echo [%~1]", "echo [%~2]", "echo [%~3]"], "two words", "", @"C:\folder with space");

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.StandardOutput.ShouldBe(["[two words]", "[]", @"[C:\folder with space]"]);
    }

    [Fact]
    public async Task RunAsync_ArgumentsWithQuotes_ReachProcessUnchanged()
    {
        var scriptPath = Path.Combine(_folder.FolderPath, "arguments.ps1");
        await File.WriteAllTextAsync(
            scriptPath, """foreach ($argument in $args) { "[$argument]" }""", TestContext.Current.CancellationToken);
        var request = new ProcessRequest
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments =
            [
                "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
                "say \"hi\"", "a \\\"b\\\" c", "end\\",
            ],
            WorkingDirectory = _folder.FolderPath,
        };

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.StandardOutput.ShouldBe(["[say \"hi\"]", "[a \\\"b\\\" c]", "[end\\]"]);
    }

    [Fact]
    public async Task RunAsync_WorkingDirectory_IsCurrentDirectoryOfProcess()
    {
        var workingDirectory = Directory.CreateDirectory(Path.Combine(_folder.FolderPath, "work")).FullName;
        var request = TestScript.Create(workingDirectory, ["echo %CD%"]);

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.StandardOutput.ShouldBe([workingDirectory]);
    }

    [Fact]
    public async Task RunAsync_EnvironmentVariable_IsVisibleToProcessOnly()
    {
        var name = $"GITHUBBACKUP_TEST_{Guid.NewGuid():N}";
        var request = TestScript.Create(_folder.FolderPath, [$"echo [%{name}%]"]) with
        {
            EnvironmentVariables = [new(name, "value with spaces")],
        };

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.StandardOutput.ShouldBe(["[value with spaces]"]);
        Environment.GetEnvironmentVariable(name).ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_CancelledWhileRunning_KillsProcessTreeAndThrows()
    {
        // ping is a child of cmd.exe and inherits its standard error. With a drain timeout longer than ping lives, the
        // run can only end before ping does if the whole tree is killed.
        var runner = new ProcessRunner(
            new SecretMasker(), new FileSystem(), TimeProvider.System, NullLogger<ProcessRunner>.Instance)
        {
            PipeDrainTimeout = TimeSpan.FromSeconds(90),
        };
        var request = TestScript.Create(_folder.FolderPath, ["echo started", "ping -n 60 127.0.0.1 >nul"]);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();

        await Should.ThrowAsync<OperationCanceledException>(() => runner.RunAsync(request, cancellation.Token));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task RunAsync_CancelledWhileOrphanHoldsPipes_ThrowsWithoutWaitingForOrphan()
    {
        // The inner cmd.exe starts ping and exits at once, so ping has no living parent in the tree of the run, yet it
        // keeps the inherited standard error open for 20 seconds after the script itself has exited. ping runs in the
        // Windows folder so that it does not lock the test folder while it outlives the test.
        var runner = new ProcessRunner(
            new SecretMasker(), new FileSystem(), TimeProvider.System, NullLogger<ProcessRunner>.Instance)
        {
            PipeDrainTimeout = TimeSpan.FromSeconds(1),
        };
        var request = TestScript.Create(
            _folder.FolderPath,
            ["cmd /d /c start \"\" /b /d \"%SystemRoot%\" ping -n 20 127.0.0.1 >nul", "echo started"]);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();

        await Should.ThrowAsync<OperationCanceledException>(() => runner.RunAsync(request, cancellation.Token));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// Deletes the temporary folder.
    /// </summary>
    public void Dispose() => _folder.Dispose();
}
