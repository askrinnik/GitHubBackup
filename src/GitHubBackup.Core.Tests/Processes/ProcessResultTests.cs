using GitHubBackup.Core.Processes;

namespace GitHubBackup.Core.Tests.Processes;

/// <summary>
/// Tests <see cref="ProcessResult"/>.
/// </summary>
public sealed class ProcessResultTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(128, false)]
    public void Succeeded_ExitCode_IsTrueOnlyForZero(int exitCode, bool expected)
    {
        var result = new ProcessResult(exitCode, TimeSpan.Zero, [], []);

        result.Succeeded.ShouldBe(expected);
    }
}
