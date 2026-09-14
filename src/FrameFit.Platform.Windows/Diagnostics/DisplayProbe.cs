using System.Runtime.InteropServices;
using System.Text;
using FrameFit.Platform.Windows.Interop;

namespace FrameFit.Platform.Windows.Diagnostics;

/// <summary>
/// בדיקת שפיות לאינטראפ הגולמי עם Windows: מפרטת בדיוק מה כל קריאה מחזירה.
/// נועדה לאבחון כאשר מיפוי המסכים מחזיר תוצאה ריקה.
/// </summary>
public static class DisplayProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO_SIMPLE
    {
        public int cbSize;
        public Native.RECT rcMonitor;
        public Native.RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfoSimple(IntPtr hMonitor, ref MONITORINFO_SIMPLE lpmi);

    /// <summary>
    /// גדלים שהאינטראפ חייב להיות תואם אליהם. גודל שגוי של מבנה הוא מקור התקלות
    /// השקטות הקלאסי ב-P/Invoke — הקריאה נכשלת בלי חריגה. לכן זהו בדיקה קבועה.
    /// </summary>
    public static IReadOnlyList<(string Name, int Actual, int Expected)> StructSizeChecks() => new[]
    {
        ("MONITORINFOEX", Marshal.SizeOf<Native.MONITORINFOEX>(), 104),
        ("MONITORINFO", Marshal.SizeOf<MONITORINFO_SIMPLE>(), 40),
        ("DISPLAY_DEVICE", Marshal.SizeOf<Native.DISPLAY_DEVICE>(), 840),
        ("DISPLAYCONFIG_PATH_INFO", Marshal.SizeOf<Native.DISPLAYCONFIG_PATH_INFO>(), 72),
        ("DISPLAYCONFIG_MODE_INFO", Marshal.SizeOf<Native.DISPLAYCONFIG_MODE_INFO>(), 64),
        ("DISPLAYCONFIG_TARGET_DEVICE_NAME", Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(), 420),
        ("DISPLAYCONFIG_SOURCE_DEVICE_NAME", Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), 84),
        ("APPBARDATA", Marshal.SizeOf<Native.APPBARDATA>(), 48)
    };

    public static string Run()
    {
        var builder = new StringBuilder();

        builder.AppendLine($"Marshal.SizeOf<MONITORINFOEX>() = {Marshal.SizeOf<Native.MONITORINFOEX>()}");
        builder.AppendLine($"Marshal.SizeOf<MONITORINFO>()   = {Marshal.SizeOf<MONITORINFO_SIMPLE>()}");
        builder.AppendLine($"Marshal.SizeOf<DISPLAY_DEVICE>() = {Marshal.SizeOf<Native.DISPLAY_DEVICE>()}");

        var callbacks = 0;
        var simpleFailures = 0;
        var exFailures = 0;
        var firstFailureError = 0;

        Native.MonitorEnumProc extendedCallback = (IntPtr monitor, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            callbacks++;

            var simple = new MONITORINFO_SIMPLE
            {
                cbSize = Marshal.SizeOf<MONITORINFO_SIMPLE>()
            };

            if (GetMonitorInfoSimple(monitor, ref simple))
            {
                builder.AppendLine(
                    $"  [MONITORINFO]    {simple.rcMonitor.Left},{simple.rcMonitor.Top} " +
                    $"{simple.rcMonitor.Width}×{simple.rcMonitor.Height} flags=0x{simple.dwFlags:X}");
            }
            else
            {
                simpleFailures++;
                firstFailureError = Marshal.GetLastWin32Error();
            }

            var extended = new Native.MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<Native.MONITORINFOEX>(),
                szDevice = string.Empty
            };

            if (Native.GetMonitorInfo(monitor, ref extended))
            {
                builder.AppendLine(
                    $"  [MONITORINFOEX]  '{extended.szDevice}' " +
                    $"{extended.rcMonitor.Left},{extended.rcMonitor.Top} " +
                    $"{extended.rcMonitor.Width}×{extended.rcMonitor.Height}");
            }
            else
            {
                exFailures++;
                firstFailureError = Marshal.GetLastWin32Error();
            }

            return true;
        };

        var enumerated = Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, extendedCallback, IntPtr.Zero);
        var enumError = enumerated ? 0 : Marshal.GetLastWin32Error();

        builder.AppendLine($"EnumDisplayMonitors -> {enumerated} (שגיאה {enumError})");
        builder.AppendLine($"callbacks = {callbacks}; כשלים: MONITORINFO={simpleFailures}, MONITORINFOEX={exFailures}, שגיאה אחרונה={firstFailureError}");

        builder.AppendLine("מתאמים לפי EnumDisplayDevices:");
        for (uint i = 0; ; i++)
        {
            var device = new Native.DISPLAY_DEVICE
            {
                cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceID = string.Empty,
                DeviceKey = string.Empty
            };

            if (!Native.EnumDisplayDevices(null, i, ref device, 0))
            {
                break;
            }

            builder.AppendLine($"  [{i}] '{device.DeviceName}' — '{device.DeviceString}' flags=0x{device.StateFlags:X}");

            for (uint j = 0; ; j++)
            {
                var monitor = new Native.DISPLAY_DEVICE
                {
                    cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>(),
                    DeviceName = string.Empty,
                    DeviceString = string.Empty,
                    DeviceID = string.Empty,
                    DeviceKey = string.Empty
                };

                if (!Native.EnumDisplayDevices(device.DeviceName, j, ref monitor, 0))
                {
                    break;
                }

                builder.AppendLine($"        מסך [{j}] '{monitor.DeviceString}' id='{monitor.DeviceID}'");
            }

            if (i > 8)
            {
                break;
            }
        }

        return builder.ToString();
    }
}
