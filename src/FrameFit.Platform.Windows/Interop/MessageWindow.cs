using System.Runtime.InteropServices;

namespace FrameFit.Platform.Windows.Interop;

/// <summary>
/// חלון "בלתי נראה" לקבלת הודעות מערכת ההפעלה: מקשי קיצור גלובליים והודעות סרגל יישום.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private static readonly Dictionary<IntPtr, MessageWindow> Instances = new();
    private static readonly Native.WndProcDelegate SharedProc = StaticWindowProc;
    private static readonly object Sync = new();

    private static string? _className;
    private static ushort _classAtom;

    private bool _disposed;

    public MessageWindow()
    {
        EnsureClassRegistered();

        Handle = Native.CreateWindowEx(
            0,
            _className!,
            "FrameFit",
            0,
            0,
            0,
            0,
            0,
            Native.HWND_MESSAGE,
            IntPtr.Zero,
            Native.GetModuleHandle(null),
            IntPtr.Zero);

        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"לא ניתן ליצור חלון הודעות (קוד שגיאה {Marshal.GetLastWin32Error()}).");
        }

        lock (Sync)
        {
            Instances[Handle] = this;
        }
    }

    public IntPtr Handle { get; }

    /// <summary>נקרא עבור כל הודעה שאינה WM_DESTROY. מחזיר null כדי לעבור לברירת המחדל.</summary>
    public Func<uint, IntPtr, IntPtr, IntPtr?>? MessageHandler { get; set; }

    public static uint RegisterMessage(string name) => Native.RegisterWindowMessage(name);

    private static void EnsureClassRegistered()
    {
        lock (Sync)
        {
            if (_className is not null)
            {
                return;
            }

            const string name = "FrameFitMessageWindow";

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
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = name,
                hIconSm = IntPtr.Zero
            };

            _classAtom = Native.RegisterClassEx(ref wndClass);
            _className = name;
        }
    }

    private static IntPtr StaticWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        MessageWindow? instance;
        lock (Sync)
        {
            Instances.TryGetValue(hWnd, out instance);
        }

        if (instance is not null)
        {
            try
            {
                var result = instance.MessageHandler?.Invoke(msg, wParam, lParam);
                if (result.HasValue)
                {
                    return result.Value;
                }
            }
            catch (Exception)
            {
                // חלון ההודעות אינו יכול להרשות לעצמו לזרוק — זה יפיל את ה-WndProc של המערכת.
            }

            if (msg == Native.WM_DESTROY)
            {
                return IntPtr.Zero;
            }
        }

        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

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

        MessageHandler = null;

        if (Handle != IntPtr.Zero)
        {
            Native.DestroyWindow(Handle);
        }
    }
}
