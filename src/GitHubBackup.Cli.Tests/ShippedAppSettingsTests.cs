using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests the <c>appsettings.json</c> that ships next to both executables.
/// </summary>
public sealed class ShippedAppSettingsTests
{
    [Fact]
    public void SerilogOverride_HostLifetime_IsWarning()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();

        configuration["Serilog:MinimumLevel:Override:Microsoft.Hosting.Lifetime"].ShouldBe("Warning");
    }
}
