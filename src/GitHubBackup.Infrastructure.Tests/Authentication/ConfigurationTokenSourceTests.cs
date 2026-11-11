using GitHubBackup.Infrastructure.Authentication;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Authentication;

/// <summary>
/// Tests <see cref="ConfigurationTokenSource"/>.
/// </summary>
public sealed class ConfigurationTokenSourceTests
{
    [Fact]
    public async Task GetTokenAsync_KeySet_ReturnsValue()
    {
        var token = await Source("token-value").GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBe("token-value");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetTokenAsync_KeyMissingOrBlank_ReturnsNull(string? value)
    {
        var token = await Source(value).GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBeNull();
    }

    [Fact]
    public async Task GetTokenAsync_SurroundingWhiteSpace_IsTrimmed()
    {
        var token = await Source(" token-value\r\n").GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBe("token-value");
    }

    [Fact]
    public void Name_NamesConfigurationKeyWithoutToken() => Source("token-value").Name.ShouldBe("configuration (GitHub:Token)");

    /// <summary>
    /// Creates the source over a configuration that sets the token key to <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The configured token, or <see langword="null"/> for no key.</param>
    /// <returns>The source.</returns>
    private static ConfigurationTokenSource Source(string? value) => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [GitHubOptions.TokenKey] = value })
            .Build());
}
