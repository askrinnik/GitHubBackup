using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;
using NSubstitute;

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

    [Fact]
    public void Create_HeadersConfigured_MasksWholeStringAndEachValue()
    {
        var masker = HostSecretMasker.Create(CreateConfiguration(OpenTelemetryOptions.HeadersKey, "api-key=first-secret, x-other=second-secret"));

        masker.Mask("Headers api-key=first-secret, x-other=second-secret.").ShouldBe("Headers ***.");
        masker.Mask("Sent first-secret and second-secret.").ShouldBe("Sent *** and ***.");
    }

    [Fact]
    public void Create_HeadersConfigured_KeepsHeaderNames()
    {
        var masker = HostSecretMasker.Create(CreateConfiguration(OpenTelemetryOptions.HeadersKey, "api-key=first-secret"));

        masker.Mask("Header api-key was rejected.").ShouldBe("Header api-key was rejected.");
    }

    [Fact]
    public void Create_PercentEncodedHeaderValue_MasksDecodedFormToo()
    {
        var masker = HostSecretMasker.Create(CreateConfiguration(OpenTelemetryOptions.HeadersKey, "x-test=fake%20value"));

        masker.Mask("Sent fake value and fake%20value.").ShouldBe("Sent *** and ***.");
    }

    [Fact]
    public void Create_MalformedHeaders_MasksSegmentValues()
    {
        var masker = HostSecretMasker.Create(CreateConfiguration(OpenTelemetryOptions.HeadersKey, "api key=first-secret,,=second-secret"));

        masker.Mask("Sent first-secret and second-secret.").ShouldBe("Sent *** and ***.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyOrWhiteSpaceHeaders_IsIgnored(string headers)
    {
        var masker = HostSecretMasker.Create(CreateConfiguration(OpenTelemetryOptions.HeadersKey, headers));

        masker.Mask("Rejected a b.").ShouldBe("Rejected a b.");
    }

    [Fact]
    public void Create_TokenAndHeaders_MasksBoth()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new(GitHubOptions.TokenKey, _opaqueToken),
                new(OpenTelemetryOptions.HeadersKey, "api-key=first-secret"),
            ])
            .Build();

        var masker = HostSecretMasker.Create(configuration);

        masker.Mask($"Sent {_opaqueToken} and first-secret.").ShouldBe("Sent *** and ***.");
    }

    [Fact]
    public void RegisterHeaders_MasksWholeStringEachValueAndDecodedValue()
    {
        var masker = new SecretMasker();

        HostSecretMasker.RegisterHeaders(masker, "api-key=first-secret,x-test=fake%20value");

        masker.Mask("Headers api-key=first-secret,x-test=fake%20value.").ShouldBe("Headers ***.");
        masker.Mask("Sent first-secret, fake%20value and fake value.").ShouldBe("Sent ***, *** and ***.");
        masker.Mask("Header api-key was rejected.").ShouldBe("Header api-key was rejected.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterHeaders_Blank_RegistersNothing(string? headers)
    {
        var masker = Substitute.For<ISecretMasker>();

        HostSecretMasker.RegisterHeaders(masker, headers);

        masker.DidNotReceiveWithAnyArgs().Register(default!);
    }

    /// <summary>
    /// Creates a configuration that sets <see cref="GitHubOptions.TokenKey"/> to <paramref name="token"/>.
    /// </summary>
    /// <param name="token">The token value.</param>
    /// <returns>The configuration.</returns>
    private static IConfigurationRoot CreateConfiguration(string token) => CreateConfiguration(GitHubOptions.TokenKey, token);

    /// <summary>
    /// Creates a configuration that sets <paramref name="key"/> to <paramref name="value"/>.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="value">The value.</param>
    /// <returns>The configuration.</returns>
    private static IConfigurationRoot CreateConfiguration(string key, string value) =>
        new ConfigurationBuilder().AddInMemoryCollection([new(key, value)]).Build();
}
