using GitHubBackup.Core;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests;

/// <summary>
/// Tests <see cref="InfrastructureServiceCollectionExtensions"/>.
/// </summary>
public sealed class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGitHubBackupInfrastructure_Sections_BindsEveryOptionsClass()
    {
        using var provider = BuildProvider(new()
        {
            ["GitHub:ApiBaseUrl"] = "https://github.example.com/api/v3",
            ["Tools:GitPath"] = @"C:\Git\cmd\git.exe",
            ["History:DatabasePath"] = @"D:\history.db",
            ["OpenTelemetry:Enabled"] = "true",
        });

        provider.GetRequiredService<IOptions<GitHubOptions>>().Value.ApiBaseUrl.ShouldBe("https://github.example.com/api/v3");
        provider.GetRequiredService<IOptions<ToolsOptions>>().Value.GitPath.ShouldBe(@"C:\Git\cmd\git.exe");
        provider.GetRequiredService<IOptions<HistoryOptions>>().Value.DatabasePath.ShouldBe(@"D:\history.db");
        provider.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_RelativePaths_ResolvesAgainstContentRoot()
    {
        using var provider = BuildProvider(new() { ["Backup:ConfigPath"] = "my-config.json" });

        provider.GetRequiredService<IOptions<BackupOptions>>().Value.ConfigPath.ShouldBe(@"C:\app\my-config.json");
        provider.GetRequiredService<IOptions<HistoryOptions>>().Value.DatabasePath.ShouldBe(@"C:\app\data\history.db");
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_InvalidSections_ReportsEveryFailureAtStartup()
    {
        using var provider = BuildProvider(new()
        {
            ["GitHub:ApiBaseUrl"] = "not a uri",
            ["Tools:SevenZipPath"] = "7z.exe",
            ["History:DatabasePath"] = "",
            ["OpenTelemetry:Enabled"] = "true",
            ["OpenTelemetry:OtlpEndpoint"] = "",
        });

        var exception = Should.Throw<AggregateException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        exception.InnerExceptions.Count.ShouldBe(4);
    }

    /// <summary>
    /// Builds a service provider with the core and infrastructure services registered over <paramref name="values"/>
    /// and a host environment whose content root is <c>C:\app\</c>.
    /// </summary>
    /// <param name="values">The configuration keys and values.</param>
    /// <returns>The service provider; the caller disposes it.</returns>
    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(@"C:\app\");

        return new ServiceCollection()
            .AddSingleton(environment)
            .AddGitHubBackupCore(configuration)
            .AddGitHubBackupInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
