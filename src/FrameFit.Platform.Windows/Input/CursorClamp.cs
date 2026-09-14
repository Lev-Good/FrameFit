using FrameFit.Core.Geometry;
using FrameFit.Platform.Windows.Interop;

namespace FrameFit.Platform.Windows.Input;

/// <summary>
/// חסימת סמן העכבר לאזור הגלוי. השפעת ClipCursor היא ברמת הדסקטופ כל עוד התהליך פעיל,
/// ו-Windows משחרר אותה אוטומטית כשהתהליך מסתיים — גם בקריסה.
/// </summary>
public static class CursorClamp
{
    public static bool IsActive { get; private set; }

    public static bool Apply(PixelRect area)
    {
        if (area.IsEmpty)
        {
            return false;
        }

        var rect = new Native.RECT(area.X, area.Y, area.Right, area.Bottom);
        IsActive = Native.ClipCursor(ref rect);
        return IsActive;
    }

    public static void Release()
    {
        Native.ClipCursor(IntPtr.Zero);
        IsActive = false;
    }

    /// <summary>
    /// האם ההגבלה שוחררה. שחרור אינו מחזיר "null" — Windows מגביל את הסמן למסך הוירטואלי
    /// המלא, ולכן זו הבדיקה הנכונה.
    /// </summary>
    public static bool IsReleased()
    {
        var current = GetCurrent();
        if (current is null)
        {
            return true;
        }

        var fullWidth = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
        var fullHeight = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);

        return current.Value.Width >= fullWidth && current.Value.Height >= fullHeight;
    }

    /// <summary>מחזיר את המלבן שהעכבר מוגבל אליו, או null אם אין הגבלה.</summary>
    public static PixelRect? GetCurrent()
    {
        if (!Native.GetClipCursor(out var rect))
        {
            return null;
        }

        var width = rect.Width;
        var height = rect.Height;
        return width <= 0 || height <= 0
            ? null
            : new PixelRect(rect.Left, rect.Top, width, height);
    }
}
