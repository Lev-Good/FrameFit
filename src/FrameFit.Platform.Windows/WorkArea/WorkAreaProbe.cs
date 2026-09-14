using System.Runtime.InteropServices;
using FrameFit.Core.Geometry;
using FrameFit.Platform.Windows.Interop;

namespace FrameFit.Platform.Windows.WorkArea;

/// <summary>
/// קריאה ישירה של אזור העבודה בפועל. זו המדידה שקובעת כמה מקום מערכת ההפעלה כבר
/// הקצתה בסרגל המשימות ובסרגלי יישום אחרים — ולכן אותה יש לקזז מההזמנה שלנו.
/// </summary>
public static class WorkAreaProbe
{
    public static PixelRect? TryGetWorkArea(PixelRect monitor)
    {
        try
        {
            var point = new Native.POINT
            {
                X = monitor.X + (monitor.Width / 2),
                Y = monitor.Y + (monitor.Height / 2)
            };

            var handle = Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var info = new Native.MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<Native.MONITORINFOEX>(),
                szDevice = string.Empty
            };

            if (!Native.GetMonitorInfo(handle, ref info))
            {
                return null;
            }

            return new PixelRect(
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Width,
                info.rcWork.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
