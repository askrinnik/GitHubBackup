using GitHubBackup.Core.Ui;

namespace GitHubBackup.Core.Tests.Ui;

/// <summary>
/// Tests <see cref="WindowPlacement"/>.
/// </summary>
public sealed class WindowPlacementTests
{
    [Theory]
    [InlineData(100, 100, 1000, 700)]
    [InlineData(-1920, -50, 400, 300)]
    [InlineData(0, 0, 10000, 10000)]
    public void IsValid_FiniteEdgesAndSizeWithinLimits_ReturnsTrue(double left, double top, double width, double height) =>
        new WindowPlacement(left, top, width, height, false).IsValid().ShouldBeTrue();

    [Theory]
    [InlineData(double.NaN, 0, 1000, 700)]
    [InlineData(0, double.PositiveInfinity, 1000, 700)]
    [InlineData(double.NegativeInfinity, 0, 1000, 700)]
    [InlineData(0, 0, 399.9, 700)]
    [InlineData(0, 0, 10000.1, 700)]
    [InlineData(0, 0, 1000, 299)]
    [InlineData(0, 0, 1000, 10001)]
    [InlineData(0, 0, double.NaN, 700)]
    [InlineData(0, 0, 1000, double.NaN)]
    public void IsValid_EdgeNotFiniteOrSizeOutOfLimits_ReturnsFalse(double left, double top, double width, double height) =>
        new WindowPlacement(left, top, width, height, true).IsValid().ShouldBeFalse();
}
