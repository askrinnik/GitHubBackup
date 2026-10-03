using System.Reflection;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Verifies that the test project resolves the <c>GitHubBackup.Cli</c> assembly it tests.
/// </summary>
public sealed class CliAssemblyTests
{
    [Fact]
    public void Load_ByName_ReturnsCliAssembly()
    {
        var assembly = Assembly.Load(new AssemblyName("GitHubBackup.Cli"));

        assembly.GetName().Name.ShouldBe("GitHubBackup.Cli");
    }
}
