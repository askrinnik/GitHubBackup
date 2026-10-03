using System.Reflection;

namespace GitHubBackup.Infrastructure.IntegrationTests;

/// <summary>
/// Verifies that the integration test project resolves the <c>GitHubBackup.Infrastructure</c> assembly it tests.
/// </summary>
public sealed class InfrastructureAssemblyTests
{
    [Fact]
    public void Load_ByName_ReturnsInfrastructureAssembly()
    {
        var assembly = Assembly.Load(new AssemblyName("GitHubBackup.Infrastructure"));

        assembly.GetName().Name.ShouldBe("GitHubBackup.Infrastructure");
    }
}
