using FrameFit.Core.Geometry;
using Xunit;

namespace FrameFit.Core.Tests;

public class MarginSetTests
{
    [Fact]
    public void Indexer_ReturnsEachSide()
    {
        var margins = new MarginSet(1, 2, 3, 4);

        Assert.Equal(1, margins[MarginSide.Left]);
        Assert.Equal(2, margins[MarginSide.Right]);
        Assert.Equal(3, margins[MarginSide.Top]);
        Assert.Equal(4, margins[MarginSide.Bottom]);
    }

    [Fact]
    public void With_ChangesOnlyTheGivenSide()
    {
        var margins = new MarginSet(1, 2, 3, 4);

        var changed = margins.With(MarginSide.Right, 99);

        Assert.Equal(new MarginSet(1, 99, 3, 4), changed);
        Assert.Equal(new MarginSet(1, 2, 3, 4), margins);
    }

    [Fact]
    public void IsAsymmetric_IsTrueWhenAnyOppositePairDiffers()
    {
        Assert.True(new MarginSet(10, 20, 0, 0).IsAsymmetric);
        Assert.True(new MarginSet(0, 0, 10, 20).IsAsymmetric);
        Assert.False(new MarginSet(10, 10, 20, 20).IsAsymmetric);
        Assert.False(MarginSet.Empty.IsAsymmetric);
    }

    [Fact]
    public void ToSymmetric_TakesTheLargestSideOfEachAxis()
    {
        var symmetric = new MarginSet(10, 20, 30, 40).ToSymmetric();

        Assert.Equal(new MarginSet(20, 20, 40, 40), symmetric);
        Assert.False(symmetric.IsAsymmetric);
    }

    [Fact]
    public void HasNegative_DetectsInvalidInput()
    {
        Assert.True(new MarginSet(0, -1, 0, 0).HasNegative);
        Assert.False(MarginSet.Empty.HasNegative);
    }

    [Fact]
    public void Totals_SumBothAxes()
    {
        var margins = new MarginSet(10, 20, 30, 40);

        Assert.Equal(30, margins.TotalHorizontal);
        Assert.Equal(70, margins.TotalVertical);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, true)]
    [InlineData(1, 0, 0, 0, false)]
    [InlineData(0, 0, 0, 2, false)]
    public void IsZero_IsTrueOnlyForAnEmptySet(int l, int r, int t, int b, bool expected)
    {
        Assert.Equal(expected, new MarginSet(l, r, t, b).IsZero);
    }
}
