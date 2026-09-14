using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FrameFit.Core.Abstractions;
using FrameFit.Core.Geometry;
using FrameFit.Platform.Windows.Interop;
using Microsoft.Win32;

namespace FrameFit.Platform.Windows.Displays;

/// <summary>
/// מיפוי המסכים של Windows: מלבנים, אזור עבודה, סוג החיבור וטביעת אצבע מה-EDID.
/// </summary>
public sealed class WindowsDisplayProvider : IDisplayProvider
{
    private const int EdidEdidBlock = 128;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var monitors = new List<Native.MONITORINFOEX>();
        var results = new List<DisplayInfo>();

        Native.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var info = new Native.MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<Native.MONITORINFOEX>(),
                szDevice = string.Empty
            };

            if (Native.GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add(info);
            }

            return true;
        };

        if (!Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            return results;
        }

        // מיפוי שם התקן GDI → (שם ידידותי, סוג חיבור) דרך QueryDisplayConfig.
        var displayConfigMap = QueryDisplayConfigMap();

        foreach (var monitor in monitors)
        {
            var deviceName = monitor.szDevice ?? string.Empty;
            displayConfigMap.TryGetValue(deviceName, out var connection);

            var friendlyName = string.IsNullOrWhiteSpace(connection.FriendlyName)
                ? deviceName
                : connection.FriendlyName;

            var (edidHash, physicalWidthMm, physicalHeightMm) = ReadEdid(deviceName);

            results.Add(new DisplayInfo(
                DeviceName: deviceName,
                FriendlyName: friendlyName,
                Bounds: new PixelRect(
                    monitor.rcMonitor.Left,
                    monitor.rcMonitor.Top,
                    monitor.rcMonitor.Width,
                    monitor.rcMonitor.Height),
                WorkArea: new PixelRect(
                    monitor.rcWork.Left,
                    monitor.rcWork.Top,
                    monitor.rcWork.Width,
                    monitor.rcWork.Height),
                IsPrimary: (monitor.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                OutputTechnology: connection.Technology ?? "לא ידוע",
                EdidHash: edidHash,
                PhysicalWidthMm: physicalWidthMm,
                PhysicalHeightMm: physicalHeightMm));
        }

        // מסך ראשי תמיד ראשון.
        results.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
        return results;
    }

    /// <summary>מחזיר את המסך שעליו יושב חלון, או null.</summary>
    public static DisplayInfo? FindDisplayForWindow(IReadOnlyList<DisplayInfo> displays, IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return null;
        }

        var hMonitor = Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
        {
            return null;
        }

        var info = new Native.MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<Native.MONITORINFOEX>(),
            szDevice = string.Empty
        };

        if (!Native.GetMonitorInfo(hMonitor, ref info))
        {
            return null;
        }

        return displays.FirstOrDefault(d =>
            string.Equals(d.DeviceName, info.szDevice, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, (string FriendlyName, string? Technology)> QueryDisplayConfigMap()
    {
        var map = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
            {
                return map;
            }

            var paths = new Native.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new Native.DISPLAYCONFIG_MODE_INFO[modeCount];

            if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return map;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];

                var source = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new Native.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id
                    },
                    viewGdiDeviceName = string.Empty
                };

                if (Native.DisplayConfigGetDeviceInfo(ref source) != 0 ||
                    string.IsNullOrWhiteSpace(source.viewGdiDeviceName))
                {
                    continue;
                }

                var target = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new Native.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    },
                    monitorFriendlyDeviceName = string.Empty,
                    monitorDevicePath = string.Empty
                };

                var technology = DescribeTechnology(path.targetInfo.outputTechnology);
                var friendly = string.Empty;

                if (Native.DisplayConfigGetDeviceInfo(ref target) == 0)
                {
                    friendly = target.monitorFriendlyDeviceName?.Trim() ?? string.Empty;
                }

                map[source.viewGdiDeviceName] = (friendly, technology);
            }
        }
        catch (Exception)
        {
            // אבחון אינו קריטי לפעולת המוצר — במקרה של כשל מחזירים מפה ריקה.
        }

        return map;
    }

    private static string DescribeTechnology(uint technology) => technology switch
    {
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 => "VGA",
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI => "DVI",
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI => "HDMI",
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS => "פנימי",
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL => "DisplayPort",
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED => "DisplayPort פנימי",
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL => "UDI",
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED => "UDI פנימי",
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL => "פנימי",
        Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER => "אחר",
        _ => "אחר"
    };

    /// <summary>
    /// קורא את ה-EDID של המסך מהרישום: מפיק טביעת אצבע וגודל פאנל פיזי (להצגת מ"מ).
    /// כשל כאן אינו מפיל דבר — מוחזרת טביעת אצבע ריקה.
    /// </summary>
    private static (string Hash, int? WidthMm, int? HeightMm) ReadEdid(string gdiDeviceName)
    {
        var edid = TryReadEdidBytes(gdiDeviceName);
        if (edid is null || edid.Length < EdidEdidBlock)
        {
            return (string.Empty, null, null);
        }

        var hash = Convert.ToHexString(SHA256.HashData(edid))[..16].ToLowerInvariant();

        // בייטים 21 ו-22 ב-EDID: גודל התמונה בס"מ.
        var widthCm = edid[21];
        var heightCm = edid[22];

        return (
            hash,
            widthCm > 0 ? widthCm * 10 : null,
            heightCm > 0 ? heightCm * 10 : null);
    }

    private static byte[]? TryReadEdidBytes(string gdiDeviceName)
    {
        try
        {
            var device = new Native.DISPLAY_DEVICE
            {
                cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceID = string.Empty,
                DeviceKey = string.Empty
            };

            if (!Native.EnumDisplayDevices(gdiDeviceName, 0, ref device, 0) ||
                string.IsNullOrWhiteSpace(device.DeviceID))
            {
                return null;
            }

            // EnumDisplayDevices מחזיר MONITOR\AUO208D\{guid}\0000, אבל מזהה הרשומה
            // בפועל ברישום הוא בעל תבנית חומרית שונה (למשל 4&28798eee&0&UID8388688).
            // לכן מחפשים לפי מזהה החומרה (AUO208D) ומאתרים את הרשומה שמכילה EDID.
            var parts = device.DeviceID.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var hardwareId = parts.Length > 1 ? parts[1] : device.DeviceID;

            var fromEnumStore = TryReadEdidFromEnumStore(hardwareId);
            if (fromEnumStore is not null)
            {
                return fromEnumStore;
            }

            // נפילה חלופית: מסלולים ישירים, למקרה של תצוגה וירטואלית או דרייבר אחר.
            var suffix = device.DeviceID.StartsWith("MONITOR\\", StringComparison.OrdinalIgnoreCase)
                ? device.DeviceID["MONITOR\\".Length..]
                : device.DeviceID;

            var candidates = new[]
            {
                $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{suffix}\Device Parameters",
                $@"SYSTEM\CurrentControlSet\Enum\{device.DeviceID}\Device Parameters"
            };

            foreach (var path in candidates)
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("EDID") is byte[] { Length: >= EdidEdidBlock } bytes)
                {
                    return bytes;
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// מאתר את ה-EDID תחת Enum\DISPLAY\&lt;מזהה חומרה&gt;\&lt;מופע&gt;\Device Parameters.
    /// </summary>
    private static byte[]? TryReadEdidFromEnumStore(string hardwareId)
    {
        using var baseKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{hardwareId}");
        if (baseKey is null)
        {
            return null;
        }

        foreach (var instanceName in baseKey.GetSubKeyNames())
        {
            using var parameters = baseKey.OpenSubKey($@"{instanceName}\Device Parameters");
            if (parameters?.GetValue("EDID") is byte[] { Length: >= EdidEdidBlock } bytes)
            {
                return bytes;
            }
        }

        return null;
    }

    /// <summary>ייצוג טקסטואלי של רשימת המסכים — משמש בדוח האבחון.</summary>
    public static string DescribeAll(IReadOnlyList<DisplayInfo> displays)
    {
        var builder = new StringBuilder();
        foreach (var display in displays)
        {
            builder.AppendLine(display.Describe());
            builder.AppendLine($"    מזהה התקן: {display.DeviceName}");
            builder.AppendLine($"    אזור עבודה: {display.WorkArea}");
            builder.AppendLine($"    EDID: {(string.IsNullOrEmpty(display.EdidHash) ? "לא זוהה" : display.EdidHash)}");
        }

        return builder.ToString();
    }
}
