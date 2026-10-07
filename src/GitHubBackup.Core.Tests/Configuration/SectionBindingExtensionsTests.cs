using GitHubBackup.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Core.Tests.Configuration;

/// <summary>
/// Tests <see cref="SectionBindingExtensions"/>.
/// </summary>
public sealed class SectionBindingExtensionsTests
{
    [Fact]
    public void BindSection_ValuesInSection_BindsProperties()
    {
        var options = Resolve(new() { ["Backup:ConfigPath"] = @"C:\backup\config.json", ["Backup:VerifyArchive"] = "false" });

        options.ConfigPath.ShouldBe(@"C:\backup\config.json");
        options.VerifyArchive.ShouldBeFalse();
    }

    [Fact]
    public void BindSection_ValueNotConvertible_ThrowsOptionsValidationExceptionNamingKey()
    {
        var exception = Should.Throw<OptionsValidationException>(() => Resolve(new() { ["Backup:ShortHashLength"] = "abc" }));

        exception.OptionsType.ShouldBe(typeof(BackupOptions));
        exception.Failures.ShouldHaveSingleItem().ShouldContain("Backup:ShortHashLength");
    }

    /// <summary>
    /// Binds <see cref="BackupOptions"/> to the <c>Backup</c> section of <paramref name="values"/> and resolves them.
    /// </summary>
    /// <param name="values">The configuration keys and values.</param>
    /// <returns>The bound options.</returns>
    private static BackupOptions Resolve(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddOptions<BackupOptions>().BindSection(configuration, BackupOptions.SectionName);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<BackupOptions>>().Value;
    }
}
