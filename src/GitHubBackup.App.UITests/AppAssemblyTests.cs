using System.Reflection;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Verifies that the UI test project resolves the <c>GitHubBackup.App</c> assembly it drives.
/// </summary>
public sealed class AppAssemblyTests
{
    [Fact]
    public void Load_ByName_ReturnsAppAssembly()
    {
        var assembly = Assembly.Load(new AssemblyName("GitHubBackup.App"));

        assembly.GetName().Name.ShouldBe("GitHubBackup.App");
    }

    [Fact]
    public void TestAssembly_HasUiCategoryTrait_SoMainCiWorkflowSkipsIt()
    {
        var trait = typeof(AppAssemblyTests).Assembly
            .GetCustomAttributes<TraitAttribute>()
            .Single(attribute => attribute.Name == "Category");

        trait.Value.ShouldBe("UI");
    }
}
