using System.IO.Abstractions;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitHubBackup.Infrastructure.IntegrationTests.Processes;

/// <summary>
/// Tests which environment variables <see cref="ProcessRunner"/> passes to a real child process.
/// </summary>
/// <remarks>
/// The variables the tests set have names no other test reads, so tests running in parallel do not interfere.
/// The child is a batch script because it must print its own environment.
/// </remarks>
public sealed class ProcessRunnerEnvironmentTests : IDisposable
{
    private const string _inheritedName = "GitHubBackup__Test__Secret";
    private const string _inheritedLowerCaseName = "githubbackup__test__lowercase";
    private const string _inheritedValue = "inherited-secret-value";

    private readonly TemporaryContentRoot _folder = new();
    private readonly ProcessRunner _runner =
        new(new SecretMasker(), new FileSystem(), TimeProvider.System, NullLogger<ProcessRunner>.Instance);

    public ProcessRunnerEnvironmentTests()
    {
        Environment.SetEnvironmentVariable(_inheritedName, _inheritedValue);
        Environment.SetEnvironmentVariable(_inheritedLowerCaseName, _inheritedValue);
    }

    [Fact]
    public async Task RunAsync_InheritedApplicationVariables_AreNotPassedToChild()
    {
        var result = await _runner.RunAsync(TestScript.Create(_folder.FolderPath, ["set"]), TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        result.StandardOutput.ShouldNotContain(line => line.StartsWith("GitHubBackup__", StringComparison.OrdinalIgnoreCase));
        result.StandardOutput.ShouldNotContain(line => line.Contains(_inheritedValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_InheritedApplicationVariables_KeepsOtherInheritedVariables()
    {
        var result = await _runner.RunAsync(TestScript.Create(_folder.FolderPath, ["set"]), TestContext.Current.CancellationToken);

        result.StandardOutput.ShouldContain(line => line.StartsWith("SystemRoot=", StringComparison.OrdinalIgnoreCase));
        result.StandardOutput.ShouldContain(line => line.StartsWith("PATH=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_RequestVariableWithApplicationPrefix_ReachesChild()
    {
        var request = TestScript.Create(_folder.FolderPath, ["echo [%GitHubBackup__Test__Explicit%]"]) with
        {
            EnvironmentVariables = [new("GitHubBackup__Test__Explicit", "explicit")],
        };

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        result.StandardOutput.ShouldBe(["[explicit]"]);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_inheritedName, null);
        Environment.SetEnvironmentVariable(_inheritedLowerCaseName, null);
        _folder.Dispose();
    }
}
