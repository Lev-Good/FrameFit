namespace FrameFit.Core.Geometry;

/// <summary>
/// הקצאה קיימת של אזור העבודה: פיקסלים שמערכת ההפעלה כבר גרעה משולי המסך —
/// סרגל המשימות, או סרגלי יישום של תוכניות אחרות. כל ערך יחסית למלבן המסך.
/// </summary>
public readonly record struct ReservedInsets(int Left, int Top, int Right, int Bottom)
{
    public static ReservedInsets None { get; } = new(0, 0, 0, 0);

    public bool IsZero => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;

    public int TotalHorizontal => Left + Right;

    public int TotalVertical => Top + Bottom;

    public int this[MarginSide side] => side switch
    {
        MarginSide.Left => Left,
        MarginSide.Top => Top,
        MarginSide.Right => Right,
        MarginSide.Bottom => Bottom,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    /// <summary>ההפרש בין מלבן המסך למלבן אזור העבודה — כלומר מה שכבר תפוס.</summary>
    public static ReservedInsets Between(PixelRect monitor, PixelRect workArea) => new(
        Math.Max(0, workArea.X - monitor.X),
        Math.Max(0, workArea.Y - monitor.Y),
        Math.Max(0, monitor.Right - workArea.Right),
        Math.Max(0, monitor.Bottom - workArea.Bottom));

    /// <summary>ההקצאה המקסימלית בכל צד — אם מערכת ההפעלה הקצתה יותר מהשוליים.</summary>
    public ReservedInsets Max(ReservedInsets other) => new(
        Math.Max(Left, other.Left),
        Math.Max(Top, other.Top),
        Math.Max(Right, other.Right),
        Math.Max(Bottom, other.Bottom));

    /// <summary>כמה פיקסלים נשארים תפוסים בכל צד *בתוך* האזור הגלוי.</summary>
    public ReservedInsets Beyond(MarginSet margins) => new(
        Math.Max(0, Left - margins.Left),
        Math.Max(0, Top - margins.Top),
        Math.Max(0, Right - margins.Right),
        Math.Max(0, Bottom - margins.Bottom));

    public override string ToString() => $"L{Left} T{Top} R{Right} B{Bottom}";
}

/// <summary>
/// התוכנית לצמצום אזור העבודה: אילו סרגלי יישום להזמין, ומהי התוצאה המצופה בפועל.
/// </summary>
/// <param name="Strips">הסרגלים שיש להזמין, אחרי קיזוז הקצאות קיימות.</param>
/// <param name="Visible">האזור הגלוי שהוגדר — המטרה.</param>
/// <param name="ExpectedWorkArea">אזור העבודה המצופה בסיום.</param>
/// <param name="Shortfall">
/// חלקים מהאזור הגלוי שההקצאה הקיימת (למשל סרגל המשימות) תופסת בכל זאת —
/// כלומר פיקסלים גלויים שהתוכנה אינה מצליחה להחזיר לשימוש.
/// </param>
/// <param name="BottomAnchor">
/// כמה פיקסלים בתחתית הוקצו במכוון לפריט שנעגן בתוך האזור הגלוי (סרגל המשימות).
/// הם אינם "חוסר": הם המקום של אותו פריט.
/// </param>
public sealed record WorkAreaPlan(
    IReadOnlyList<(MarginSide Side, PixelRect Rect)> Strips,
    PixelRect Visible,
    PixelRect ExpectedWorkArea,
    ReservedInsets Shortfall,
    int BottomAnchor = 0)
{
    /// <summary>האם התוכן לבדו ממלא את כל האזור הגלוי.</summary>
    public bool IsExact => Shortfall.IsZero && BottomAnchor == 0;
}

/// <summary>
/// חישוב הצמצום של אזור העבודה. הליבה כאן היא ההבחנה בין <b>השוליים המוסתרים</b>
/// לבין <b>מה שמערכת ההפעלה כבר הקצתה</b>:
///
/// מערכת ההפעלה מפנה מקום לסרגל המשימות *בתוך* אזור העבודה עוד לפני ש-FrameFit נוגע
/// במשהו. סרגל יישום שמוזמן על אותו צד נדחק מעל אותו סרגל קיים — ולכן הזמנת שוליים
/// בגובה מלא הייתה מקטינה את אזור העבודה ביותר מהנדרש, ומשאירה רצועה מתה בתוך האזור
/// הגלוי. לכן כל צד מוזמן בגובהה של השוליים **פחות** מה שכבר הוקצה בו.
/// </summary>
public static class WorkAreaCalculator
{
    /// <summary>הסבילות (בפיקסלים) להשוואה בין אזור עבודה בפועל לזה שתוכנן.</summary>
    public const int DefaultTolerance = 2;

    /// <param name="bottomAnchor">
    /// גובה פריט שנעגן בתוך האזור הגלוי בתחתיתו (סרגל המשימות). המקום שלו נגרע
    /// מאזור העבודה, אך הוא אינו נספר כ"חוסר" — הוא בשימוש מכוון ונראה לעין.
    /// </param>
    public static WorkAreaPlan Plan(PixelRect monitor, MarginSet margins, ReservedInsets existing, int bottomAnchor = 0)
    {
        var anchor = Math.Max(0, bottomAnchor);
        var visible = VisibleAreaCalculator.Compute(monitor, margins);

        // ההקצאה בתחתית היא סכום ההקצאה הקיימת (המקום הטבעי של סרגל המשימות),
        // השוליים שהוגדרו, והמקום שנתפס בידי הפריט המעוגן בתוך האזור הגלוי.
        var reserved = new ReservedInsets(
            Math.Max(existing.Left, margins.Left),
            Math.Max(existing.Top, margins.Top),
            Math.Max(existing.Right, margins.Right),
            Math.Max(existing.Bottom, margins.Bottom + anchor));

        var shortfall = existing.Beyond(margins);

        // פריט מעוגן תופס מקום באזור הגלוי במכוון, ולכן אינו נחשב לגזילה.
        if (anchor > 0)
        {
            shortfall = shortfall with { Bottom = 0 };
        }

        var expected = new PixelRect(
            monitor.X + reserved.Left,
            monitor.Y + reserved.Top,
            monitor.Width - reserved.TotalHorizontal,
            monitor.Height - reserved.Top - reserved.Bottom);

        var strips = new List<(MarginSide, PixelRect)>(4);

        // כל סרגל ממוקם בצמוד להקצאה הקיימת באותו צד — כך איחוד השניים ממלא בדיוק את השוליים.
        var left = margins.Left - existing.Left;
        if (left > 0)
        {
            strips.Add((MarginSide.Left, new PixelRect(monitor.X + existing.Left, monitor.Y, left, monitor.Height)));
        }

        var right = margins.Right - existing.Right;
        if (right > 0)
        {
            strips.Add((MarginSide.Right, new PixelRect(monitor.Right - margins.Right, monitor.Y, right, monitor.Height)));
        }

        var top = margins.Top - existing.Top;
        if (top > 0)
        {
            strips.Add((MarginSide.Top, new PixelRect(monitor.X + margins.Left, monitor.Y + existing.Top, visible.Width, top)));
        }

        var bottom = margins.Bottom + anchor - existing.Bottom;
        if (bottom > 0)
        {
            strips.Add((MarginSide.Bottom, new PixelRect(
                monitor.X + margins.Left,
                monitor.Bottom - reserved.Bottom,
                visible.Width,
                bottom)));
        }

        return new WorkAreaPlan(strips, visible, expected, shortfall, anchor);
    }

    /// <summary>האם אזור העבודה בפועל הוא בדיוק זה שתוכנן.</summary>
    public static bool Matches(WorkAreaPlan plan, PixelRect actual, int tolerance = DefaultTolerance) =>
        Math.Abs(actual.X - plan.ExpectedWorkArea.X) <= tolerance &&
        Math.Abs(actual.Y - plan.ExpectedWorkArea.Y) <= tolerance &&
        Math.Abs(actual.Width - plan.ExpectedWorkArea.Width) <= tolerance &&
        Math.Abs(actual.Height - plan.ExpectedWorkArea.Height) <= tolerance;

    /// <summary>
    /// דרישת המינימום: שום פיקסל של אזור העבודה אינו נוגע באזור המוסתר. זה מה שחייב
    /// להתקיים בכל מקרה — גם אם סרגל המשימות מצליח לתפוס חלק מהאזור הגלוי.
    /// </summary>
    public static bool StaysInside(WorkAreaPlan plan, PixelRect actual, int tolerance = DefaultTolerance) =>
        Covers(plan.Visible, actual, tolerance);

    /// <summary>האם <paramref name="inner"/> כולו בתוך <paramref name="outer"/> (עם סבילות).</summary>
    public static bool Covers(PixelRect outer, PixelRect inner, int tolerance = DefaultTolerance) =>
        inner.X >= outer.X - tolerance &&
        inner.Y >= outer.Y - tolerance &&
        inner.Right <= outer.Right + tolerance &&
        inner.Bottom <= outer.Bottom + tolerance;
}
