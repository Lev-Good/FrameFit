namespace FrameFit.Core.Geometry;

/// <summary>סוג הבעיה בסט השוליים.</summary>
public enum MarginIssue
{
    None,
    Negative,
    TooSmall
}

public readonly record struct MarginValidation(MarginIssue Issue, MarginSide? Side)
{
    public static MarginValidation Ok { get; } = new(MarginIssue.None, null);

    public bool IsValid => Issue == MarginIssue.None;
}

/// <summary>
/// כל חישובי האזור הגלוי. זהו ליבת המוצר והוא נבדק ביחידה.
/// </summary>
public static class VisibleAreaCalculator
{
    /// <summary>השטח הגלוי המינימלי שמותר להשאיר (רוחב).</summary>
    public const int MinimumWidth = 640;

    /// <summary>השטח הגלוי המינימלי שמותר להשאיר (גובה).</summary>
    public const int MinimumHeight = 480;

    /// <summary>האזור הגלוי = מלבן המסך פחות ארבעת השוליים.</summary>
    public static PixelRect Compute(PixelRect monitor, MarginSet margins) => new(
        monitor.X + margins.Left,
        monitor.Y + margins.Top,
        monitor.Width - margins.TotalHorizontal,
        monitor.Height - margins.TotalVertical);

    /// <summary>בדיקת תקינות מלאה של סט השוליים עבור מסך נתון.</summary>
    public static MarginValidation Validate(PixelRect monitor, MarginSet margins)
    {
        foreach (var side in MarginSet.AllSides)
        {
            if (margins[side] < 0)
            {
                return new MarginValidation(MarginIssue.Negative, side);
            }
        }

        var visible = Compute(monitor, margins);
        if (visible.Width < MinimumWidth || visible.Height < MinimumHeight)
        {
            // מציינים את הצד הגדול ביותר באותו ציר — זה שגורם לבעיה.
            var side = visible.Width < MinimumWidth
                ? (margins.Left >= margins.Right ? MarginSide.Left : MarginSide.Right)
                : (margins.Top >= margins.Bottom ? MarginSide.Top : MarginSide.Bottom);
            return new MarginValidation(MarginIssue.TooSmall, side);
        }

        return MarginValidation.Ok;
    }

    /// <summary>
    /// הערך המרבי המותר לצד נתון, בהתחשב בשלושת האחרים. זה מה שמגביל את הסליידר בממשק.
    /// </summary>
    public static int MaximumForSide(PixelRect monitor, MarginSet margins, MarginSide side)
    {
        var max = side switch
        {
            MarginSide.Left => monitor.Width - MinimumWidth - margins.Right,
            MarginSide.Right => monitor.Width - MinimumWidth - margins.Left,
            MarginSide.Top => monitor.Height - MinimumHeight - margins.Bottom,
            MarginSide.Bottom => monitor.Height - MinimumHeight - margins.Top,
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };

        return Math.Max(0, max);
    }

    /// <summary>מחזיר את סט השוליים המקורי, או כזה שמוגבל לשטח גלוי תקין.</summary>
    public static MarginSet Clamp(PixelRect monitor, MarginSet margins)
    {
        var result = MarginSet.Empty;
        foreach (var side in MarginSet.AllSides)
        {
            var wanted = Math.Max(0, margins[side]);
            var allowed = MaximumForSide(monitor, result, side);
            result = result.With(side, Math.Min(wanted, allowed));
        }

        return result;
    }

    /// <summary>
    /// אחוז ההקטנה של השטח הגלוי ביחס למסך — לשקיפות מול המשתמש.
    /// </summary>
    public static double VisibleRatio(PixelRect monitor, MarginSet margins)
    {
        if (monitor.Area == 0)
        {
            return 0d;
        }

        return (double)Compute(monitor, margins).Area / monitor.Area;
    }

    /// <summary>המרת פיקסלים למילימטרים, כאשר רוחב הפאנל הפיזי ידוע. אחרת null.</summary>
    public static double? PixelsToMillimeters(int pixels, int monitorWidthPixels, int? physicalWidthMm)
    {
        if (physicalWidthMm is null or <= 0 || monitorWidthPixels <= 0)
        {
            return null;
        }

        return (double)pixels * physicalWidthMm.Value / monitorWidthPixels;
    }
}
