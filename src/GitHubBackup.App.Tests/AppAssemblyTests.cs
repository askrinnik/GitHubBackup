using System.Reflection;

namespace GitHubBackup.App.Tests;

/// <summary>
/// Verifies that the test project resolves the <c>GitHubBackup.App</c> assembly it tests.
/// </summary>
public sealed class AppAssemblyTests
{
    [Fact]
    public void Load_ByName_ReturnsAppAssembly()
    {
        var assembly = Assembly.Load(new AssemblyName("GitHubBackup.App"));

        assembly.GetName().Name.ShouldBe("GitHubBackup.App");
    }
}
