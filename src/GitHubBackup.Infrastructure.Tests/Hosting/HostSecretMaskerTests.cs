using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Hosting;

/// <summary>
/// Tests <see cref="HostSecretMasker"/>.
/// </summary>
public sealed class HostSecretMaskerTests
{
    /// <summary>A token without a recognisable format, masked only because the configuration sets it.</summary>
    private const string _opaqueToken = "opaque-token-without-format";

    [Fact]
    public void Create_TokenConfigured_MasksIt()
    {
        var masker = HostSecretMasker.Create(CreateConfiguration(_opaqueToken));

        masker.Mask($"Rejected {_opaqueToken}.").ShouldBe("Rejected ***.");
    }

    [Fact]
    public void Create_NoToken_LeavesTextUnchanged()
    {
        var masker = HostSecretMasker.Create(new ConfigurationBuilder().Build());

        masker.Mask($"Rejected {_opaqueToken}.").ShouldBe($"Rejected {_opaqueToken}.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyOrWhiteSpaceToken_IsIgnored(string token)
    {
        var masker = HostSecretMasker.Create(CreateConfiguration(token));

        masker.Mask("Rejected a b.").ShouldBe("Rejected a b.");
    }

    /// <summary>
    /// Creates a configuration that sets <see cref="GitHubOptions.TokenKey"/> to <paramref name="token"/>.
    /// </summary>
    /// <param name="token">The token value.</param>
    /// <returns>The configuration.</returns>
    private static IConfigurationRoot CreateConfiguration(string token) =>
        new ConfigurationBuilder().AddInMemoryCollection([new(GitHubOptions.TokenKey, token)]).Build();
}
