using System.Runtime.InteropServices;
using FrameFit.Core.Geometry;
using FrameFit.Platform.Windows.Interop;

namespace FrameFit.Platform.Windows.WorkArea;

/// <summary>
/// שכבה 1 — צמצום "אזור העבודה" של Windows דרך סרגלי יישום (AppBar).
/// מרגע שהאזור מצומצם, מקסם, סרגל המשימות ו-Snap מכבדים את השוליים בלי שום התערבות נוספת.
///
/// מנגנון זה מסומן באפיון כניסיוני (ספיק S0.1) ולכן הוא כבוי כברירת מחדל בממשק.
/// </summary>
public sealed class WorkAreaHost : IDisposable
{
    private const string CallbackMessageName = "FrameFitAppBarMessage";

    private readonly List<AppBar> _bars = new();
    private bool _disposed;
    private PixelRect _monitor;
    private MarginSet _margins = MarginSet.Empty;

    public bool IsActive => _bars.Count > 0;

    /// <summary>
    /// מנסה להזמין את ארבעת השוליים כמקום שמור. מחזיר false עם הסבר אם משהו נכשל.
    /// </summary>
    public bool TryApply(PixelRect monitor, MarginSet margins, out string message)
    {
        Revert();

        _monitor = monitor;
        _margins = margins;

        var strips = BuildStrips(monitor, margins);
        var callbackMessage = MessageWindow.RegisterMessage(CallbackMessageName);

        try
        {
            foreach (var (edge, rect) in strips)
            {
                var bar = new AppBar(edge, callbackMessage);
                if (!bar.TryRegister(rect, out var error))
                {
                    message = $"הזמנת השוליים נכשלה (צד {edge}): {error}";
                    Revert();
                    return false;
                }

                _bars.Add(bar);
            }
        }
        catch (Exception ex)
        {
            message = $"הזמנת השוליים נכשלה: {ex.Message}";
            Revert();
            return false;
        }

        message = "אזור העבודה צומצם בהצלחה.";
        return true;
    }

    /// <summary>מסיר את כל הסרגלים ומחזיר את אזור העבודה למצבו המקורי.</summary>
    public void Revert()
    {
        foreach (var bar in _bars)
        {
            bar.Dispose();
        }

        _bars.Clear();
    }

    private static List<(uint Edge, PixelRect Rect)> BuildStrips(PixelRect monitor, MarginSet margins)
    {
        var visibleWidth = Math.Max(0, monitor.Width - margins.TotalHorizontal);
        var strips = new List<(uint, PixelRect)>(4);

        if (margins.Left > 0)
        {
            strips.Add((Native.ABE_LEFT, new PixelRect(monitor.X, monitor.Y, margins.Left, monitor.Height)));
        }

        if (margins.Right > 0)
        {
            strips.Add((Native.ABE_RIGHT, new PixelRect(monitor.Right - margins.Right, monitor.Y, margins.Right, monitor.Height)));
        }

        if (margins.Top > 0)
        {
            strips.Add((Native.ABE_TOP, new PixelRect(monitor.X + margins.Left, monitor.Y, visibleWidth, margins.Top)));
        }

        if (margins.Bottom > 0)
        {
            strips.Add((Native.ABE_BOTTOM, new PixelRect(
                monitor.X + margins.Left,
                monitor.Bottom - margins.Bottom,
                visibleWidth,
                margins.Bottom)));
        }

        return strips;
    }

    /// <summary>
    /// האם אזור העובדה הנוכחי *נמצא בתוך* האזור הגלוי — כלומר שום פיקסל שלו אינו
    /// נוגע באזור המוסתר.
    ///
    /// זו הדרישה האמיתית, ולא שוויון מדויק: אזור העבודה של Windows גם מפנה מקום
    /// לסרגל המשימות, ולכן הוא עשוי להיות קטן יותר מהאזור הגלוי — וזה מצב תקין
    /// ואף רצוי. הבעיה היחידה היא אם הוא חורג אל תוך השוליים המוסתרים.
    /// </summary>
    public static bool WorkAreaRespectsMargins(PixelRect monitor, PixelRect workArea, MarginSet margins)
    {
        var visible = VisibleAreaCalculator.Compute(monitor, margins);
        const int tolerance = 2;

        return workArea.X >= visible.X - tolerance &&
               workArea.Y >= visible.Y - tolerance &&
               workArea.Right <= visible.Right + tolerance &&
               workArea.Bottom <= visible.Bottom + tolerance;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Revert();
    }

    /// <summary>סרגל יישום בודד — חלון הודעות אחד לכל צד.</summary>
    private sealed class AppBar : IDisposable
    {
        private readonly uint _edge;
        private readonly uint _callbackMessage;
        private readonly MessageWindow _window;
        private PixelRect _rect;
        private bool _registered;

        public AppBar(uint edge, uint callbackMessage)
        {
            _edge = edge;
            _callbackMessage = callbackMessage;
            _window = new MessageWindow { MessageHandler = OnMessage };
        }

        public bool TryRegister(PixelRect rect, out string error)
        {
            _rect = rect;

            var data = CreateData();
            if (Native.SHAppBarMessage(Native.ABM_NEW, ref data) == UIntPtr.Zero)
            {
                error = "המערכת סירבה לרשום סרגל יישום.";
                return false;
            }

            _registered = true;

            if (!SetPosition(out error))
            {
                return false;
            }

            NotifyWorkAreaChanged();
            error = string.Empty;
            return true;
        }

        private bool SetPosition(out string error)
        {
            var data = CreateData();

            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref data);

            // המערכת מחזירה מיקום מוצע; אם הוא שונה מדי, נכבד את המקסימום שנותר.
            switch (_edge)
            {
                case Native.ABE_LEFT:
                    data.rc.Right = data.rc.Left + _rect.Width;
                    break;
                case Native.ABE_RIGHT:
                    data.rc.Left = data.rc.Right - _rect.Width;
                    break;
                case Native.ABE_TOP:
                    data.rc.Bottom = data.rc.Top + _rect.Height;
                    break;
                case Native.ABE_BOTTOM:
                    data.rc.Top = data.rc.Bottom - _rect.Height;
                    break;
            }

            if (Native.SHAppBarMessage(Native.ABM_SETPOS, ref data) == UIntPtr.Zero)
            {
                error = "המערכת סירבה לקבוע את מיקום הסרגל.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private Native.APPBARDATA CreateData() => new()
        {
            cbSize = Marshal.SizeOf<Native.APPBARDATA>(),
            hWnd = _window.Handle,
            uCallbackMessage = _callbackMessage,
            uEdge = _edge,
            rc = new Native.RECT(_rect.X, _rect.Y, _rect.Right, _rect.Bottom),
            lParam = IntPtr.Zero
        };

        /// <summary>שולח ABN_POSCHANGED לכל החלונות כדי שיידעו שהאזור השתנה.</summary>
        private static void NotifyWorkAreaChanged()
        {
            var data = new Native.APPBARDATA
            {
                cbSize = Marshal.SizeOf<Native.APPBARDATA>()
            };

            Native.SHAppBarMessage(Native.ABN_POSCHANGED, ref data);
        }

        private IntPtr? OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == _callbackMessage)
            {
                switch ((uint)lParam.ToInt64())
                {
                    case Native.ABN_POSCHANGED:
                        // מישהו אחר שינה את אזור העבודה — מייצבים את שלנו מחדש.
                        SetPosition(out _);
                        return IntPtr.Zero;
                    case Native.ABN_FULLSCREENAPP:
                    case Native.ABN_WINDOWARRANGE:
                        return IntPtr.Zero;
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (_registered)
            {
                var data = CreateData();
                Native.SHAppBarMessage(Native.ABM_REMOVE, ref data);
                _registered = false;
            }

            _window.Dispose();
        }
    }
}
