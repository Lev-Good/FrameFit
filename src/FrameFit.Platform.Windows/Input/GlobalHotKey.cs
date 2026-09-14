using System.Runtime.InteropServices;
using FrameFit.Platform.Windows.Interop;

namespace FrameFit.Platform.Windows.Input;

/// <summary>
/// מקש קיצור גלובלי. זהו מנגנון הבטיחות המרכזי (D4): הוא עובד גם כשסמן העכבר חסום
/// ואינו תלוי בכך שחלון הניהול יהיה ממוקד.
/// </summary>
public sealed class GlobalHotKey : IDisposable
{
    /// <summary>הצירוף של מקש החירום: Ctrl + Alt + Shift.</summary>
    public const uint PanicModifiers = 0x0001 | 0x0002 | 0x0004;

    /// <summary>מקש F12.</summary>
    public const uint PanicVirtualKey = 0x7B;

    private readonly MessageWindow _window;
    private readonly int _id;
    private bool _disposed;

    public GlobalHotKey(uint modifiers, uint virtualKey, Action callback, int id = 0xF1)
    {
        _id = id;
        Callback = callback;
        _window = new MessageWindow();
        _window.MessageHandler = OnMessage;

        IsRegistered = Native.RegisterHotKey(_window.Handle, _id, modifiers | Native.MOD_NOREPEAT, virtualKey);
        LastError = IsRegistered ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
    }

    public bool IsRegistered { get; }

    public int LastError { get; }

    public Action Callback { get; }

    private IntPtr? OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == _id)
        {
            try
            {
                Callback();
            }
            catch (Exception)
            {
                // לעולם לא לזרוק מתוך WndProc.
            }

            return IntPtr.Zero;
        }

        return null;
    }

    /// <summary>
    /// בודק אם אפשר לרשום את הצירוף, ומשחרר מיד. משמש לבדיקה עצמית
    /// בלי להשאיר מקש תפוס.
    /// </summary>
    public static bool Probe(uint modifiers, uint virtualKey, out int error)
    {
        const int id = 0x7E;

        using var window = new MessageWindow();
        var registered = Native.RegisterHotKey(window.Handle, id, modifiers, virtualKey);
        error = registered ? 0 : Marshal.GetLastWin32Error();

        if (registered)
        {
            Native.UnregisterHotKey(window.Handle, id);
        }

        return registered;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (IsRegistered)
        {
            Native.UnregisterHotKey(_window.Handle, _id);
        }

        _window.Dispose();
    }
}
