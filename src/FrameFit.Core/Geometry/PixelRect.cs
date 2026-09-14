namespace FrameFit.Core.Geometry;

/// <summary>
/// מלבן בקואורדינטות דסקטופ וירטואלי, בפיקסלים פיזיים.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    /// <summary>האם המלבן כולו בתוך מלבן אחר.</summary>
    public bool IsWithin(PixelRect other) =>
        X >= other.X && Y >= other.Y && Right <= other.Right && Bottom <= other.Bottom;

    /// <summary>חישוב המלבן המשותף, או מלבן ריק אם אין חפיפה.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top
            ? new PixelRect(left, top, 0, 0)
            : new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>איזו שקיפה מהמלבן הזה נמצא בתוך <paramref name="other"/> (0.0–1.0).</summary>
    public double CoverageOf(PixelRect other)
    {
        if (other.Area == 0)
        {
            return 0d;
        }

        return (double)Intersect(other).Area / other.Area;
    }

    public override string ToString() => $"{X},{Y} {Width}×{Height}";
}
