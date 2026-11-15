using GitHubBackup.Infrastructure.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="OtlpHeaders"/>.
/// </summary>
public sealed class OtlpHeadersTests
{
    [Fact]
    public void TryParse_OnePair_ReturnsIt()
    {
        var parsed = OtlpHeaders.TryParse("api-key=abc123", out var pairs);

        parsed.ShouldBeTrue();
        pairs.ShouldBe([new("api-key", "abc123")]);
    }

    [Fact]
    public void TryParse_SeveralPairsWithWhiteSpace_ReturnsTrimmedPairsInOrder()
    {
        var parsed = OtlpHeaders.TryParse(" api-key = abc123 ,x-other=value two\t", out var pairs);

        parsed.ShouldBeTrue();
        pairs.ShouldBe([new("api-key", "abc123"), new("x-other", "value two")]);
    }

    [Fact]
    public void TryParse_EqualsSignInValue_SplitsAtFirstOne()
    {
        var parsed = OtlpHeaders.TryParse("authorization=Basic abc==", out var pairs);

        parsed.ShouldBeTrue();
        pairs.ShouldBe([new("authorization", "Basic abc==")]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("api-key")]
    [InlineData("=abc")]
    [InlineData("  =abc")]
    [InlineData("api-key=")]
    [InlineData("api-key=   ")]
    [InlineData("a=b,,c=d")]
    [InlineData("a=b,")]
    [InlineData(",a=b")]
    [InlineData("api key=abc")]
    [InlineData("api:key=abc")]
    [InlineData("api-key=ab\rc")]
    [InlineData("api-key=ab\nc")]
    [InlineData("api-key=ab\u0000c")]
    [InlineData("api-key=ab%0D%0Ac")]
    [InlineData("api-key=%20")]
    [InlineData("api%20key=abc")]
    public void TryParse_Malformed_ReturnsFalseAndNoPairs(string headers)
    {
        var parsed = OtlpHeaders.TryParse(headers, out var pairs);

        parsed.ShouldBeFalse();
        pairs.ShouldBeEmpty();
    }

    [Fact]
    public void SegmentValues_MalformedList_ReturnsTextAfterFirstEqualsSignOfEachSegment()
    {
        var values = OtlpHeaders.SegmentValues("api-key=abc==, no-separator ,=lonely, empty= ,a b=c d");

        values.ShouldBe(["abc==", "lonely", "c d"]);
    }
}
