using GitHubBackup.Core.Ui;

namespace GitHubBackup.Core.Tests.Ui;

/// <summary>
/// Tests <see cref="UiSettings"/>.
/// </summary>
public sealed class UiSettingsTests
{
    private static readonly WindowPlacement _placement = new(100, 50, 1000, 700, true);

    [Fact]
    public void Default_ReturnsSystemThemeDefaultPlacementAndHeights() =>
        UiSettings.Default.ShouldBe(new UiSettings(UiTheme.System, null, 160, 220));

    [Fact]
    public void Sanitize_ValidValues_ReturnsEqualSettings()
    {
        var settings = new UiSettings(UiTheme.Dark, _placement, 300, 400);

        settings.Sanitize().ShouldBe(settings);
    }

    [Fact]
    public void Sanitize_UndefinedTheme_ReturnsSystem() =>
        (UiSettings.Default with { Theme = (UiTheme)42 }).Sanitize().Theme.ShouldBe(UiTheme.System);

    [Fact]
    public void Sanitize_InvalidPlacement_ReturnsDefaultPlacementAndKeepsOtherValues()
    {
        var settings = new UiSettings(UiTheme.Light, _placement with { Width = 100 }, 300, 400);

        settings.Sanitize().ShouldBe(new UiSettings(UiTheme.Light, null, 300, 400));
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(2000, 2000)]
    [InlineData(59.9, 160)]
    [InlineData(2000.1, 160)]
    [InlineData(double.NaN, 160)]
    [InlineData(double.PositiveInfinity, 160)]
    public void Sanitize_LogPanelHeight_KeepsValueWithinLimitsOrReturnsDefault(double height, double expected) =>
        (UiSettings.Default with { LogPanelHeight = height }).Sanitize().LogPanelHeight.ShouldBe(expected);

    [Theory]
    [InlineData(80, 80)]
    [InlineData(2000, 2000)]
    [InlineData(79, 220)]
    [InlineData(2001, 220)]
    [InlineData(double.NaN, 220)]
    [InlineData(double.NegativeInfinity, 220)]
    public void Sanitize_SummaryPanelHeight_KeepsValueWithinLimitsOrReturnsDefault(double height, double expected) =>
        (UiSettings.Default with { SummaryPanelHeight = height }).Sanitize().SummaryPanelHeight.ShouldBe(expected);

    [Fact]
    public void Sanitize_OneHeightInvalid_KeepsTheOther()
    {
        var settings = new UiSettings(UiTheme.System, null, 10, 500);

        settings.Sanitize().ShouldBe(new UiSettings(UiTheme.System, null, 160, 500));
    }
}
