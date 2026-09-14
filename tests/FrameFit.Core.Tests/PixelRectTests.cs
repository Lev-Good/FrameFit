using FrameFit.Core.Geometry;
using Xunit;

namespace FrameFit.Core.Tests;

public class PixelRectTests
{
    [Fact]
    public void Edges_AreDerivedFromPositionAndSize()
    {
        var rect = new PixelRect(10, 20, 100, 50);

        Assert.Equal(110, rect.Right);
        Assert.Equal(70, rect.Bottom);
        Assert.Equal(5000, rect.Area);
    }

    [Fact]
    public void Intersect_ReturnsOverlap()
    {
        var a = new PixelRect(0, 0, 100, 100);
        var b = new PixelRect(50, 50, 100, 100);

        var overlap = a.Intersect(b);

        Assert.Equal(new PixelRect(50, 50, 50, 50), overlap);
    }

    [Fact]
    public void Intersect_ReturnsEmptyRectWhenThereIsNoOverlap()
    {
        var a = new PixelRect(0, 0, 10, 10);
        var b = new PixelRect(100, 100, 10, 10);

        Assert.True(a.Intersect(b).IsEmpty);
    }

    [Fact]
    public void CoverageOf_MeasuresHowMuchOfTheOtherRectIsCovered()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var windowCoveringHalf = new PixelRect(0, 0, 960, 1080);

        Assert.Equal(0.5d, windowCoveringHalf.CoverageOf(monitor), 5);
        Assert.Equal(1d, monitor.CoverageOf(monitor), 5);
    }

    [Fact]
    public void IsWithin_DetectsContainment()
    {
        var outer = new PixelRect(0, 0, 100, 100);

        Assert.True(new PixelRect(10, 10, 50, 50).IsWithin(outer));
        Assert.False(new PixelRect(50, 50, 100, 100).IsWithin(outer));
    }

    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        Assert.Equal(new PixelRect(1, 2, 3, 4), new PixelRect(1, 2, 3, 4));
        Assert.NotEqual(new PixelRect(1, 2, 3, 4), new PixelRect(1, 2, 3, 5));
    }
}
