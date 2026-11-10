using GitHubBackup.Core.Processes;

namespace GitHubBackup.Core.Tests.Processes;

/// <summary>
/// Tests <see cref="ProcessEnvironmentVariable"/>.
/// </summary>
public sealed class ProcessEnvironmentVariableTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToString_AnyVariable_LeavesValueOut(bool isSecret)
    {
        var variable = new ProcessEnvironmentVariable("GIT_CONFIG_VALUE_0", "Authorization: Basic c2VjcmV0", isSecret);

        variable.ToString().ShouldBe($"ProcessEnvironmentVariable {{ Name = GIT_CONFIG_VALUE_0, IsSecret = {isSecret} }}");
    }
}
