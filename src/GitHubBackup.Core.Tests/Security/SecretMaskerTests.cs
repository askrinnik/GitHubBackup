using System.Text.Json;
using GitHubBackup.Core.Security;

namespace GitHubBackup.Core.Tests.Security;

/// <summary>
/// Tests <see cref="SecretMasker"/>.
/// </summary>
public sealed class SecretMaskerTests
{
    /// <summary>A token in the classic GitHub format.</summary>
    private const string _classicToken = "ghp_TestTokenValue0123456789abcdefABCDEF";

    private readonly SecretMasker _masker = new();

    [Fact]
    public void Mask_RegisteredSecret_ReplacesEveryOccurrence()
    {
        _masker.Register("s3cret-value");

        _masker.Mask("a s3cret-value and s3cret-value again").ShouldBe("a *** and *** again");
    }

    [Fact]
    public void Mask_RegisteredSecretsOverlap_ReplacesLongerSecretAsWhole()
    {
        _masker.Register("abc");
        _masker.Register("abcdef");

        _masker.Mask("x abcdef y abc").ShouldBe("x *** y ***");
    }

    [Fact]
    public void Mask_SecretRegisteredTwice_ReplacesIt()
    {
        _masker.Register("s3cret");
        _masker.Register("s3cret");

        _masker.Mask("s3cret").ShouldBe(SecretMasker.Replacement);
    }

    [Theory]
    [InlineData("Authorization: Bearer abc.def-ghi_jkl", "Authorization: Bearer ***")]
    [InlineData("authorization: basic b3BhcXVlLXZhbHVl", "authorization: basic ***")]
    [InlineData("Authorization: token abc123", "Authorization: token ***")]
    [InlineData(
        "http.https://github.com/.extraHeader=Authorization: Basic dGVzdC12YWx1ZQ==",
        "http.https://github.com/.extraHeader=Authorization: Basic ***")]
    [InlineData("""{"Authorization":"Bearer abc"}""", """{"Authorization":"Bearer ***"}""")]
    public void Mask_AuthorizationHeader_MasksValueAndKeepsScheme(string text, string expected) =>
        _masker.Mask(text).ShouldBe(expected);

    [Theory]
    [InlineData(_classicToken)]
    [InlineData("gho_TestTokenValue0123456789abcdefABCDEF")]
    [InlineData("ghs_TestTokenValue0123456789abcdefABCDEF")]
    [InlineData("github_pat_11ABCDEFG0123456789_abcdefghijklmnopqrstuvwxyz")]
    public void Mask_UnregisteredGitHubToken_ReplacesIt(string token) =>
        _masker.Mask($"token={token};").ShouldBe("token=***;");

    [Theory]
    [InlineData("Authorization required")]
    [InlineData("ghp_short")]
    [InlineData("Cloning octocat/hello-world into C:\\backup")]
    public void Mask_TextWithoutSecrets_ReturnsTextUnchanged(string text) => _masker.Mask(text).ShouldBe(text);

    [Fact]
    public void Mask_SecretsInsideJsonStrings_KeepsJsonValid()
    {
        var json = $$"""{"@m":"Header Authorization: Bearer {{_classicToken}}","Token":"{{_classicToken}}"}""";

        var masked = _masker.Mask(json);

        using var document = JsonDocument.Parse(masked);
        document.RootElement.GetProperty("@m").GetString().ShouldBe("Header Authorization: Bearer ***");
        document.RootElement.GetProperty("Token").GetString().ShouldBe(SecretMasker.Replacement);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Register_EmptySecret_Throws(string secret) => Should.Throw<ArgumentException>(() => _masker.Register(secret));
}
