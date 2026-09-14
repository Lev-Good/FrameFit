using System.Globalization;
using System.Runtime.InteropServices;
using FrameFit.Core.Geometry;
using FrameFit.Platform.Windows.Interop;

namespace FrameFit.Platform.Windows.Overlay;

/// <summary>
/// רצועת כיסוי אחת מעל אחד השוליים. החלון שקוף לקלט (WS_EX_TRANSPARENT),
/// אינו מופיע ב-Alt+Tab (WS_EX_TOOLWINDOW) ואינו מקבל מיקוד (WS_EX_NOACTIVATE).
/// במצב כיול הוא מצייר סרגל עם ציון פיקסלים, כדי ליישר מול המסגרת הפיזית.
/// </summary>
internal sealed class OverlayStrip : IDisposable
{
    private static readonly Dictionary<IntPtr, OverlayStrip> Instances = new();
    private static readonly Native.WndProcDelegate SharedProc = StaticWindowProc;
    private static readonly object Sync = new();

    private static string? _className;

    private bool _disposed;
    private byte _opacity = 255;
    private bool _showRulers = true;
    private int _rulerOrigin;
    private bool _horizontal;

    public OverlayStrip(MarginSide side)
    {
        Side = side;
        _horizontal = side is MarginSide.Top or MarginSide.Bottom;

        EnsureClassRegistered();

        var exStyle = Native.WS_EX_LAYERED
                      | Native.WS_EX_TRANSPARENT
                      | Native.WS_EX_NOACTIVATE
                      | Native.WS_EX_TOOLWINDOW
                      | Native.WS_EX_TOPMOST;

        Handle = Native.CreateWindowEx(
            (uint)exStyle,
            _className!,
            "FrameFitOverlay",
            Native.WS_POPUP,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            Native.GetModuleHandle(null),
            IntPtr.Zero);

        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"לא ניתן ליצור שכבת כיסוי (קוד שגיאה {Marshal.GetLastWin32Error()}).");
        }

        lock (Sync)
        {
            Instances[Handle] = this;
        }
    }

    public MarginSide Side { get; }

    public IntPtr Handle { get; }

    private static void EnsureClassRegistered()
    {
        lock (Sync)
        {
            if (_className is not null)
            {
                return;
            }

            const string name = "FrameFitOverlayWindow";

            // רקע שחור כברירת מחדל של המחלקה — מונע הבהוב בין ציורים.
            var wndClass = new Native.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Native.WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(SharedProc),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = Native.GetModuleHandle(null),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = Native.GetStockObject(Native.BLACK_BRUSH),
                lpszMenuName = null,
                lpszClassName = name,
                hIconSm = IntPtr.Zero
            };

            Native.RegisterClassEx(ref wndClass);
            _className = name;
        }
    }

    /// <summary>קביעת הגבולות והצגה/הסתרה. נקרא בזמן אמת בזמן גרירת סליידר.</summary>
    public void SetBounds(int x, int y, int width, int height, bool visible, byte opacity, bool showRulers, int rulerOrigin)
    {
        _opacity = opacity;
        _showRulers = showRulers;
        _rulerOrigin = rulerOrigin;

        if (_disposed || Handle == IntPtr.Zero)
        {
            return;
        }

        var hidden = !visible || width <= 0 || height <= 0;
        if (hidden)
        {
            Native.ShowWindow(Handle, Native.SW_HIDE);
            return;
        }

        Native.SetLayeredWindowAttributes(Handle, 0, _opacity, Native.LWA_ALPHA);

        Native.SetWindowPos(
            Handle,
            Native.HWND_TOPMOST,
            x,
            y,
            width,
            height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        Native.InvalidateRect(Handle, IntPtr.Zero, true);
    }

    public void Hide()
    {
        if (!_disposed && Handle != IntPtr.Zero)
        {
            Native.ShowWindow(Handle, Native.SW_HIDE);
        }
    }

    private static IntPtr StaticWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        OverlayStrip? instance;
        lock (Sync)
        {
            Instances.TryGetValue(hWnd, out instance);
        }

        if (instance is not null)
        {
            try
            {
                if (msg == Native.WM_PAINT)
                {
                    instance.OnPaint();
                    return IntPtr.Zero;
                }

                if (msg == Native.WM_DESTROY)
                {
                    return IntPtr.Zero;
                }
            }
            catch (Exception)
            {
                // תקלה בציור אסור שתיפול את התוכנית.
            }
        }

        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnPaint()
    {
        var hdc = Native.BeginPaint(Handle, out var paint);
        try
        {
            Native.GetWindowRect(Handle, out var rect);
            Native.FillRect(hdc, ref rect, Native.GetStockObject(Native.BLACK_BRUSH));

            if (_showRulers && !rect.IsEmpty)
            {
                DrawRuler(hdc, rect);
            }
        }
        finally
        {
            Native.EndPaint(Handle, ref paint);
        }
    }

    /// <summary>
    /// מצייר סרגל פיקסלים לאורך הקצה הפנימי של הרצועה: קו גבול, שנתות כל 10 פיקסלים,
    /// שנתה ארוכה וציון מספרי כל 100 פיקסלים — בקואורדינטות של המסך.
    /// </summary>
    private void DrawRuler(IntPtr hdc, Native.RECT rect)
    {
        var white = Native.GetStockObject(Native.WHITE_BRUSH);
        Native.SetBkMode(hdc, Native.TRANSPARENT);

        var fontSize = Native.SelectObject(hdc, Native.GetStockObject(Native.DEFAULT_GUI_FONT));

        try
        {
            if (_horizontal)
            {
                var innerY = Side == MarginSide.Top ? rect.Bottom - 3 : rect.Top;
                var edgeLine = NewRect(rect.Left, innerY, rect.Right, innerY + 3);
                Native.FillRect(hdc, ref edgeLine, white);

                for (var x = rect.Left; x < rect.Right; x += 10)
                {
                    var offset = x - rect.Left;
                    var isHundred = offset % 100 == 0;
                    var isFifty = offset % 50 == 0;
                    var length = isHundred ? 14 : isFifty ? 9 : 4;

                    var tickY = Side == MarginSide.Top ? rect.Bottom - 3 - length : rect.Top + 3;
                    var tick = NewRect(x, tickY, x + 2, tickY + length);
                    Native.FillRect(hdc, ref tick, white);

                    if (isHundred && rect.Height >= 16)
                    {
                        var label = (_rulerOrigin + offset).ToString(CultureInfo.InvariantCulture);
                        var textY = Side == MarginSide.Top ? rect.Bottom - 20 : rect.Top + 16;
                        Native.SetTextColor(hdc, 0x00FFFFFF);
                        Native.TextOut(hdc, x + 3, textY, label, label.Length);
                    }
                }
            }
            else
            {
                var innerX = Side == MarginSide.Left ? rect.Right - 3 : rect.Left;
                var edgeLine = NewRect(innerX, rect.Top, innerX + 3, rect.Bottom);
                Native.FillRect(hdc, ref edgeLine, white);

                var roomForText = rect.Width >= 30;

                for (var y = rect.Top; y < rect.Bottom; y += 10)
                {
                    var offset = y - rect.Top;
                    var isHundred = offset % 100 == 0;
                    var isFifty = offset % 50 == 0;
                    var length = isHundred ? 14 : isFifty ? 9 : 4;

                    var tickX = Side == MarginSide.Left ? rect.Right - 3 - length : rect.Left + 3;
                    var tick = NewRect(tickX, y, tickX + length, y + 2);
                    Native.FillRect(hdc, ref tick, white);

                    if (isHundred && roomForText)
                    {
                        var label = (_rulerOrigin + offset).ToString(CultureInfo.InvariantCulture);
                        var textX = Side == MarginSide.Left ? rect.Left + 2 : rect.Left + 18;
                        Native.SetTextColor(hdc, 0x00FFFFFF);
                        Native.TextOut(hdc, textX, y + 2, label, label.Length);
                    }
                }
            }
        }
        finally
        {
            Native.SelectObject(hdc, fontSize);
        }
    }

    private static Native.RECT NewRect(int left, int top, int right, int bottom) =>
        new(left, top, right, bottom);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (Sync)
        {
            Instances.Remove(Handle);
        }

        if (Handle != IntPtr.Zero)
        {
            Native.DestroyWindow(Handle);
        }
    }
}
