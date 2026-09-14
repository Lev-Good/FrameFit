using System.Runtime.InteropServices;
using System.Text;
using FrameFit.Core.Abstractions;
using FrameFit.Core.Geometry;
using FrameFit.Platform.Windows.Displays;
using FrameFit.Platform.Windows.Input;
using FrameFit.Platform.Windows.Interop;
using Microsoft.Win32;

namespace FrameFit.Platform.Windows.Diagnostics;

/// <summary>
/// דוח אבחון לטכנאי: חומרה, חיבורים, אזור עבודה ומצב מנגנוני האכיפה.
/// זה הכלי שמאפשר להבין מרחוק למה שוליים לא מתנהגים כמצופה.
/// </summary>
public static class DiagnosticsService
{
    public static IReadOnlyList<string> GetAdapterNames()
    {
        var names = new List<string>();

        try
        {
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

                if ((device.StateFlags & 0x8) != 0)
                {
                    // מתאם מירור — לא רלוונטי.
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(device.DeviceString)
                    ? device.DeviceName
                    : device.DeviceString;

                var version = TryReadDriverVersion(device.DeviceKey);
                names.Add(string.IsNullOrEmpty(version) ? label : $"{label} (דרייבר {version})");
            }
        }
        catch (Exception)
        {
            // אבחון בלבד.
        }

        return names;
    }

    private static string TryReadDriverVersion(string? deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
        {
            return string.Empty;
        }

        try
        {
            var path = deviceKey
                .Replace(@"\Registry\Machine\", string.Empty, StringComparison.OrdinalIgnoreCase)
                .TrimStart('\\');

            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue("DriverVersion") as string ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public static string BuildReport(
        IReadOnlyList<DisplayInfo> displays,
        DisplayInfo? target,
        MarginSet margins,
        bool overlayActive,
        bool workAreaActive,
        bool cursorClamped,
        bool watcherActive,
        int refitCount,
        int uncooperativeCount)
    {
        var builder = new StringBuilder();

        builder.AppendLine("FrameFit — דוח אבחון");
        builder.AppendLine($"נבנה: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Windows: {Environment.OSVersion.Version}");
        builder.AppendLine($"גרסת .NET: {Environment.Version}");
        builder.AppendLine();

        builder.AppendLine("כרטיסי מסך:");
        var adapters = GetAdapterNames();
        if (adapters.Count == 0)
        {
            builder.AppendLine("  לא זוהו");
        }
        else
        {
            foreach (var adapter in adapters)
            {
                builder.AppendLine($"  • {adapter}");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"מסכים ({displays.Count}):");
        builder.Append(WindowsDisplayProvider.DescribeAll(displays));

        builder.AppendLine();
        builder.AppendLine("הגדרה נוכחית:");
        if (target is null)
        {
            builder.AppendLine("  אין מסך נבחר.");
        }
        else
        {
            var visible = VisibleAreaCalculator.Compute(target.Bounds, margins);
            var validation = VisibleAreaCalculator.Validate(target.Bounds, margins);
            var symmetric = margins.ToSymmetric();

            builder.AppendLine($"  מסך נבחר: {target.Describe()}");
            builder.AppendLine($"  שוליים: {margins}");
            builder.AppendLine($"  לא-סימטרי: {(margins.IsAsymmetric ? "כן" : "לא")}");
            builder.AppendLine($"  אזור גלוי: {visible} ({VisibleAreaCalculator.VisibleRatio(target.Bounds, margins):P1} מהמסך)");
            builder.AppendLine($"  תקינות השוליים: {(validation.IsValid ? "תקין" : $"שגיאה — {DescribeIssue(validation)}")}");
            builder.AppendLine($"  חלופה סימטרית (אם הייתה נדרשת עבודת דרייבר): {symmetric}");

            var actualWorkArea = GetWorkArea(target) ?? target.WorkArea;
            builder.AppendLine($"  אזור עבודה בפועל: {actualWorkArea}");
            builder.AppendLine($"  אזור העבודה בתוך האזור הגלוי: {(WorkArea.WorkAreaHost.WorkAreaRespectsMargins(target.Bounds, actualWorkArea, margins) ? "כן" : "לא")}");
        }

        builder.AppendLine();
        builder.AppendLine("מצב מנגנוני האכיפה:");
        builder.AppendLine($"  שכבת כיסוי שחורה: {(overlayActive ? "פעילה" : "כבויה")}");
        builder.AppendLine($"  צמצום אזור עבודה (AppBar): {(workAreaActive ? "פעיל" : "כבוי")}");
        builder.AppendLine($"  חסימת סמן העכבר: {(cursorClamped ? "פעילה" : "כבויה")}");
        builder.AppendLine($"  שומר מסך-מלא: {(watcherActive ? "פעיל" : "כבוי")}");
        builder.AppendLine($"  חלונות שהותאמו מאז ההפעלה: {refitCount}");
        builder.AppendLine($"  תוכניות שמתנגדות לאכיפה: {uncooperativeCount}");

        return builder.ToString();
    }

    private static string DescribeIssue(MarginValidation validation) => validation.Issue switch
    {
        MarginIssue.Negative => $"שוליים שליליים (צד {validation.Side})",
        MarginIssue.TooSmall => $"האזור הגלוי קטן מהמינימום (צד {validation.Side})",
        _ => "לא ידוע"
    };

    /// <summary>קורא את אזור העבודה הנוכחי של המסך ישירות ממערכת ההפעלה.</summary>
    public static PixelRect? GetWorkArea(DisplayInfo display)
    {
        try
        {
            var point = new Native.POINT
            {
                X = display.Bounds.X + (display.Bounds.Width / 2),
                Y = display.Bounds.Y + (display.Bounds.Height / 2)
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

    /// <summary>מצב חסימת העכבר כפי שמדווח על ידי מערכת ההפעלה.</summary>
    public static PixelRect? GetCursorClampRect() => CursorClamp.GetCurrent();
}
