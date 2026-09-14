using FrameFit.Core.Geometry;
using Xunit;

namespace FrameFit.Core.Tests;

public class VisibleAreaCalculatorTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Compute_ReturnsMonitorMinusMargins()
    {
        var margins = new MarginSet(38, 64, 22, 14);

        var visible = VisibleAreaCalculator.Compute(Monitor, margins);

        Assert.Equal(38, visible.X);
        Assert.Equal(22, visible.Y);
        Assert.Equal(1920 - 38 - 64, visible.Width);
        Assert.Equal(1080 - 22 - 14, visible.Height);
    }

    [Fact]
    public void Compute_OnSecondaryMonitor_KeepsDesktopOffset()
    {
        var secondary = new PixelRect(1920, 0, 1920, 1080);

        var visible = VisibleAreaCalculator.Compute(secondary, new MarginSet(10, 20, 30, 40));

        Assert.Equal(1930, visible.X);
        Assert.Equal(30, visible.Y);
        Assert.Equal(1920 - 30, visible.Width);
        Assert.Equal(1080 - 70, visible.Height);
    }

    [Fact]
    public void Compute_WithEmptyMargins_EqualsWholeMonitor()
    {
        Assert.Equal(Monitor, VisibleAreaCalculator.Compute(Monitor, MarginSet.Empty));
    }

    [Fact]
    public void Validate_RejectsNegativeMargin_AndPointsAtTheSide()
    {
        var result = VisibleAreaCalculator.Validate(Monitor, new MarginSet(0, -5, 0, 0));

        Assert.False(result.IsValid);
        Assert.Equal(MarginIssue.Negative, result.Issue);
        Assert.Equal(MarginSide.Right, result.Side);
    }

    [Fact]
    public void Validate_RejectsMarginsThatLeaveTooLittleArea()
    {
        var result = VisibleAreaCalculator.Validate(Monitor, new MarginSet(0, 0, 0, 1080));

        Assert.False(result.IsValid);
        Assert.Equal(MarginIssue.TooSmall, result.Issue);
        Assert.Equal(MarginSide.Bottom, result.Side);
    }

    [Fact]
    public void Validate_AcceptsExactlyMinimumVisibleArea()
    {
        var margins = new MarginSet(1920 - 640, 0, 1080 - 480, 0);

        Assert.True(VisibleAreaCalculator.Validate(Monitor, margins).IsValid);
    }

    [Fact]
    public void Validate_AcceptsAsymmetricMargins()
    {
        Assert.True(VisibleAreaCalculator.Validate(Monitor, new MarginSet(38, 64, 22, 14)).IsValid);
    }

    [Theory]
    [InlineData(MarginSide.Left)]
    [InlineData(MarginSide.Right)]
    [InlineData(MarginSide.Top)]
    [InlineData(MarginSide.Bottom)]
    public void MaximumForSide_LeavesRoomForMinimumVisibleArea(MarginSide side)
    {
        var margins = new MarginSet(0, 0, 0, 0);

        var max = VisibleAreaCalculator.MaximumForSide(Monitor, margins, side);
        var candidate = margins.With(side, max);

        Assert.True(VisibleAreaCalculator.Validate(Monitor, candidate).IsValid);
        var over = margins.With(side, max + 1);
        Assert.False(VisibleAreaCalculator.Validate(Monitor, over).IsValid);
    }

    [Fact]
    public void MaximumForSide_AccountsForTheOppositeSide()
    {
        var margins = new MarginSet(0, 64, 0, 0);

        var max = VisibleAreaCalculator.MaximumForSide(Monitor, margins, MarginSide.Left);

        Assert.Equal(1920 - 640 - 64, max);
    }

    [Fact]
    public void Clamp_ReducesMarginsUntilTheResultIsValid()
    {
        var clamped = VisibleAreaCalculator.Clamp(Monitor, new MarginSet(5000, 5000, 5000, 5000));

        Assert.True(VisibleAreaCalculator.Validate(Monitor, clamped).IsValid);
        Assert.Equal(new MarginSet(1280, 0, 600, 0), clamped);
    }

    [Fact]
    public void Clamp_LeavesAValidSetUntouched()
    {
        var margins = new MarginSet(38, 64, 22, 14);

        Assert.Equal(margins, VisibleAreaCalculator.Clamp(Monitor, margins));
    }

    [Fact]
    public void Clamp_TurnsNegativeValuesIntoTowardZero()
    {
        var clamped = VisibleAreaCalculator.Clamp(Monitor, new MarginSet(-10, -10, -10, -10));

        Assert.Equal(MarginSet.Empty, clamped);
    }

    [Fact]
    public void VisibleRatio_ReflectsHowMuchOfTheScreenRemains()
    {
        var ratio = VisibleAreaCalculator.VisibleRatio(Monitor, new MarginSet(96, 96, 0, 0));

        Assert.Equal(1d - (192d / 1920d), ratio, 5);
    }

    [Fact]
    public void PixelsToMillimeters_UsesPhysicalPanelSizeWhenKnown()
    {
        var millimeters = VisibleAreaCalculator.PixelsToMillimeters(96, 1920, 521);

        Assert.NotNull(millimeters);
        Assert.Equal(26.05, millimeters.Value, 2);
    }

    [Fact]
    public void PixelsToMillimeters_ReturnsNullWhenPhysicalSizeUnknown()
    {
        Assert.Null(VisibleAreaCalculator.PixelsToMillimeters(96, 1920, null));
        Assert.Null(VisibleAreaCalculator.PixelsToMillimeters(96, 0, 521));
    }
}
