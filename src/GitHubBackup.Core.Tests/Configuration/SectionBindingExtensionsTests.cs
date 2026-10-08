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

    [Theory]
    [InlineData("Backup:ShortHashLength", "abc", "Backup:ShortHashLength cannot be converted to System.Int32.")]
    [InlineData("Backup:VerifyArchive", "maybe", "Backup:VerifyArchive cannot be converted to System.Boolean.")]
    public void BindSection_ValueNotConvertible_NamesKeyAndTypeWithoutValue(string key, string value, string expected)
    {
        var exception = Should.Throw<OptionsValidationException>(() => Resolve(new() { [key] = value }));

        var failure = exception.Failures.ShouldHaveSingleItem();
        failure.ShouldBe(expected);
        failure.ShouldNotContain(value);
    }

    [Fact]
    public void BindSection_TokenAsValueOfTypedKey_DoesNotRevealToken()
    {
        const string token = "ghp_TestTokenValue0123456789abcdefABCDEF";

        var exception = Should.Throw<OptionsValidationException>(() => Resolve(new() { ["Backup:ShortHashLength"] = token }));

        exception.Failures.ShouldHaveSingleItem().ShouldBe("Backup:ShortHashLength cannot be converted to System.Int32.");
    }

    [Fact]
    public void BindSection_ValueImitatingBinderMessage_DoesNotReplaceKey()
    {
        var exception = Should.Throw<OptionsValidationException>(() => Resolve(new()
        {
            ["Backup:ShortHashLength"] = "c' at 'Backup:ConfigPath' to type 'System.Foo",
            ["Backup:ConfigPath"] = "c",
        }));

        exception.Failures.ShouldHaveSingleItem().ShouldBe("Backup:ShortHashLength cannot be converted to System.Int32.");
    }

    [Fact]
    public void BindSection_ValueImitatingTemplateWithLongerDecoyKey_DoesNotLeakValueFragment()
    {
        var exception = Should.Throw<OptionsValidationException>(() => Resolve(new()
        {
            ["Backup:ShortHashLength"] = "c' at 'Backup:LongerKey' to type 'SECRET",
            ["Backup:LongerKey"] = "c",
        }));

        var failure = exception.Failures.ShouldHaveSingleItem();
        failure.ShouldBe("Backup:ShortHashLength cannot be converted to System.Int32.");
        failure.ShouldNotContain("SECRET");
        failure.ShouldNotContain("LongerKey");
    }

    [Fact]
    public void DescribeBindingFailure_TypeContainsQuote_NamesSectionAndOptionsTypeOnly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Backup:LongerKey"] = "c" })
            .Build();

        var message = SectionBindingExtensions.DescribeBindingFailure<BackupOptions>(
            configuration.GetSection(BackupOptions.SectionName),
            new InvalidOperationException("Failed to convert configuration value 'c' at 'Backup:LongerKey' to type 'SECRET' at 'x' to type 'System.Int32'."));

        message.ShouldBe("The Backup section cannot be bound to BackupOptions.");
    }

    [Fact]
    public void DescribeBindingFailure_MessageMatchesNoLeaf_NamesSectionAndOptionsTypeOnly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Backup:ShortHashLength"] = "ghp_secret" })
            .Build();

        var message = SectionBindingExtensions.DescribeBindingFailure<BackupOptions>(
            configuration.GetSection(BackupOptions.SectionName),
            new InvalidOperationException("unexpected text with ghp_secret"));

        message.ShouldBe("The Backup section cannot be bound to BackupOptions.");
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
