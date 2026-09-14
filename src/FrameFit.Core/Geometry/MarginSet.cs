namespace FrameFit.Core.Geometry;

/// <summary>צד של המסך.</summary>
public enum MarginSide
{
    Left,
    Top,
    Right,
    Bottom
}

/// <summary>
/// ארבעת השוליים המוסתרים, בפיקסלים. כל צד נקבע בנפרד.
/// </summary>
public readonly record struct MarginSet(int Left, int Right, int Top, int Bottom)
{
    public static MarginSet Empty { get; } = new(0, 0, 0, 0);

    public static IReadOnlyList<MarginSide> AllSides { get; } =
        new[] { MarginSide.Left, MarginSide.Right, MarginSide.Top, MarginSide.Bottom };

    public bool IsZero => Left == 0 && Right == 0 && Top == 0 && Bottom == 0;

    public bool HasNegative => Left < 0 || Right < 0 || Top < 0 || Bottom < 0;

    /// <summary>השוליים האופקיים ביחד.</summary>
    public int TotalHorizontal => Left + Right;

    /// <summary>השוליים האנכיים ביחד.</summary>
    public int TotalVertical => Top + Bottom;

    /// <summary>האם השוליים אינם סימטריים — כלומר נדרש מנוע החלונות ולא מנוע הדרייבר.</summary>
    public bool IsAsymmetric =>
        Left != Right || Top != Bottom;

    public int this[MarginSide side] => side switch
    {
        MarginSide.Left => Left,
        MarginSide.Top => Top,
        MarginSide.Right => Right,
        MarginSide.Bottom => Bottom,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    public MarginSet With(MarginSide side, int value) => side switch
    {
        MarginSide.Left => this with { Left = value },
        MarginSide.Top => this with { Top = value },
        MarginSide.Right => this with { Right = value },
        MarginSide.Bottom => this with { Bottom = value },
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    /// <summary>
    /// השוליים הסימטריים המקבילים — אם היינו נאלצים לפתור את הבעיה ברמת הדרייבר,
    /// היינו צריכים להסתיר בכל צד את המקסימום של אותו ציר.
    /// </summary>
    public MarginSet ToSymmetric() =>
        new(Math.Max(Left, Right), Math.Max(Left, Right), Math.Max(Top, Bottom), Math.Max(Top, Bottom));

    public override string ToString() =>
        $"L{Left} R{Right} T{Top} B{Bottom}";
}
