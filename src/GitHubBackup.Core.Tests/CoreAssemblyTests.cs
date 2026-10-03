using System.Reflection;

namespace GitHubBackup.Core.Tests;

/// <summary>
/// Verifies that the test project resolves the <c>GitHubBackup.Core</c> assembly it tests.
/// </summary>
public sealed class CoreAssemblyTests
{
    [Fact]
    public void Load_ByName_ReturnsCoreAssembly()
    {
        var assembly = Assembly.Load(new AssemblyName("GitHubBackup.Core"));

        assembly.GetName().Name.ShouldBe("GitHubBackup.Core");
    }
}
