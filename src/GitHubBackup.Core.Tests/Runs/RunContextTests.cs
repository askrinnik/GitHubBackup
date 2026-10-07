using GitHubBackup.Core.Runs;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="RunContext"/>.
/// </summary>
public sealed class RunContextTests
{
    [Fact]
    public void RunId_NewInstance_IsNotEmpty() => new RunContext().RunId.ShouldNotBe(Guid.Empty);

    [Fact]
    public void RunId_ReadTwice_ReturnsSameValue()
    {
        var context = new RunContext();

        context.RunId.ShouldBe(context.RunId);
    }

    [Fact]
    public void RunId_TwoInstances_Differ() => new RunContext().RunId.ShouldNotBe(new RunContext().RunId);
}
