using FrameFit.Core.Geometry;

namespace FrameFit.Platform.Windows.Overlay;

/// <summary>איך נראית שכבת הכיסוי כרגע.</summary>
/// <param name="Opacity">0 = שקוף, 255 = שחור מלא. בתצוגה מקדימה משתמשים بعרך חלקי.</param>
/// <param name="ShowRulers">האם לצייר סרגל פיקסלים לאורך הקצה הפנימי (מצב כיול).</param>
public readonly record struct OverlayStyle(int Opacity, bool ShowRulers)
{
    /// <summary>שחור מלא, בלי סרגלים — מצב האכיפה.</summary>
    public static OverlayStyle Solid { get; } = new(255, false);

    /// <summary>תצוגה מקדימה: שחור כמעט אטום, עם סרגלים ליישור מול המסגרת.</summary>
    public static OverlayStyle Preview { get; } = new(220, true);
}

/// <summary>
/// ארבע רצועות הכיסוי מעל השוליים המוסתרים. זה מה שעושה את השוליים לשחורים וכבויים.
/// </summary>
public sealed class OverlayHost : IDisposable
{
    private readonly Dictionary<MarginSide, OverlayStrip> _strips = new();
    private bool _disposed;
    private bool _visible;
    private PixelRect _monitor;
    private MarginSet _margins = MarginSet.Empty;
    private OverlayStyle _style = OverlayStyle.Preview;

    public bool IsVisible => _visible;

    /// <summary>
    /// הגבולות בפועל של ארבע הרצועות, כפי שנקבעו בחלונות. משמש לבדיקה עצמית
    /// ולאבחון — כדי לאמת שהגיאומטריה אכן הוחלה על המסך ולא רק חושבה.
    /// </summary>
    public IReadOnlyList<(MarginSide Side, PixelRect Rect)> GetStripRects()
    {
        var result = new List<(MarginSide, PixelRect)>();

        foreach (var pair in _strips)
        {
            var strip = pair.Value;

            if (strip.Handle == IntPtr.Zero ||
                !Interop.Native.GetWindowRect(strip.Handle, out var rect))
            {
                continue;
            }

            result.Add((pair.Key, new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height)));
        }

        return result;
    }

    /// <summary>מעדכן את הגיאומטריה והסגנון. נקרא בכל שינוי בסליידרים, בזמן אמת.</summary>
    public void Apply(PixelRect monitor, MarginSet margins, OverlayStyle style, bool visible)
    {
        if (_disposed)
        {
            return;
        }

        _monitor = monitor;
        _margins = margins;
        _style = style;
        _visible = visible;

        var visibleWidth = Math.Max(0, monitor.Width - margins.TotalHorizontal);

        ApplyStrip(MarginSide.Left, monitor.X, monitor.Y, margins.Left, monitor.Height, monitor.Y);
        ApplyStrip(MarginSide.Right, monitor.Right - margins.Right, monitor.Y, margins.Right, monitor.Height, monitor.Y);
        ApplyStrip(MarginSide.Top, monitor.X + margins.Left, monitor.Y, visibleWidth, margins.Top, monitor.X);
        ApplyStrip(MarginSide.Bottom, monitor.X + margins.Left, monitor.Bottom - margins.Bottom, visibleWidth, margins.Bottom, monitor.X);
    }

    private void ApplyStrip(MarginSide side, int x, int y, int width, int height, int rulerOrigin)
    {
        if (!_strips.TryGetValue(side, out var strip))
        {
            strip = new OverlayStrip(side);
            _strips[side] = strip;
        }

        strip.SetBounds(x, y, width, height, _visible, (byte)Math.Clamp(_style.Opacity, 0, 255), _style.ShowRulers, rulerOrigin);
    }

    /// <summary>מסתיר את כל הרצועות בלי להשמיד אותן — חזרה מהירה.</summary>
    public void Hide()
    {
        _visible = false;
        foreach (var strip in _strips.Values)
        {
            strip.Hide();
        }
    }

    /// <summary>מציג מחדש את הרצועות לפי המצב האחרון.</summary>
    public void Show(OverlayStyle style)
    {
        if (_disposed)
        {
            return;
        }

        Apply(_monitor, _margins, style, visible: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var strip in _strips.Values)
        {
            strip.Dispose();
        }

        _strips.Clear();
    }
}
