using System.Reflection;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// Verifies that every production assembly whose dependencies the architecture rules inspect is resolvable.
/// </summary>
public sealed class SolutionAssemblyTests
{
    [Theory]
    [InlineData("GitHubBackup.Core")]
    [InlineData("GitHubBackup.Infrastructure")]
    [InlineData("GitHubBackup.Cli")]
    [InlineData("GitHubBackup.App")]
    public void Load_ProductionAssemblyName_ReturnsAssembly(string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));

        assembly.GetName().Name.ShouldBe(assemblyName);
    }
}
