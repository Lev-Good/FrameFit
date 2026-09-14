using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FrameFit.Core.Abstractions;
using FrameFit.Core.Geometry;
using FrameFit.Platform.Windows;
using FrameFit.Platform.Windows.Diagnostics;
using FrameFit.Platform.Windows.Displays;
using FrameFit.Platform.Windows.Input;
using FrameFit.Platform.Windows.Interop;
using FrameFit.Platform.Windows.Overlay;
using FrameFit.Platform.Windows.WorkArea;

namespace FrameFit.App;

/// <summary>
/// בדיקה עצמית: מאמתת בפועל את האינטראפ עם Windows — ולא רק שהקוד מתקמפל.
/// רצה מ-הטרמינל עם ‎--self-test ומחזירה קוד יציאה.
///
/// הבדיקה מציגה את שכבת הכיסוי לזמן קצר כדי לוודא שהיא באמת מופיעה ומוקמת נכון.
/// אפשר לבטל זאת עם ‎--no-flash, ואת בדיקת אזור העבודה עם ‎--no-workarea-probe.
/// </summary>
internal static class SelfTest
{
    private static readonly List<(string Name, bool Passed, string Detail)> Results = new();

    private static readonly System.Text.StringBuilder Transcript = new();

    private static string _outputPath = string.Empty;

    /// <summary>
    /// פלט הבדיקה נכתב גם לקונסולה וגם לקובץ. אפליקציית WPF אינה מקצה קונסולה,
    /// ולכן הקובץ הוא הדרך האמינה לראות את התוצאות.
    /// </summary>
    private static void Write(string? text = null)
    {
        Transcript.AppendLine(text ?? string.Empty);

        try
        {
            Console.WriteLine(text);
        }
        catch (Exception)
        {
            // אין קונסולה — הקובץ הוא הערוץ.
        }
    }

    private static void ResolveOutputPath(string[] args)
    {
        var explicitPath = args.FirstOrDefault(a => a.StartsWith("--out=", StringComparison.OrdinalIgnoreCase));

        _outputPath = explicitPath is not null
            ? explicitPath["--out=".Length..].Trim('"')
            : System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "framefit-selftest.txt");
    }

    private static void FlushTranscript()
    {
        try
        {
            System.IO.File.WriteAllText(_outputPath, Transcript.ToString());
        }
        catch (Exception)
        {
            // אין עוד לאן לדווח.
        }
    }

    public static int Run(string[] args)
    {
        Results.Clear();

        var flash = !HasFlag(args, "--no-flash");
        var probeWorkArea = !HasFlag(args, "--no-workarea-probe");

        ResolveOutputPath(args);

        Write();
        Write("=== FrameFit — בדיקה עצמית ===");
        Write();

        RunStructSizeCheck();

        Write("--- בדיקת אינטראפ גולמית ---");
        Write(DisplayProbe.Run());
        Write("--- סוף בדיקת האינטראפ ---");
        Write();

        var displays = RunDisplayCheck();
        if (displays.Count == 0)
        {
            Report();
            Write($"הפלט נשמר בקובץ: {_outputPath}");
            FlushTranscript();
            return 1;
        }

        var target = displays.FirstOrDefault(d => !d.IsPrimary) ?? displays[0];

        RunGeometryCheck(target);
        RunOverlayCheck(target, flash);

        if (probeWorkArea)
        {
            RunWorkAreaCheck(target);
        }
        else
        {
            Record("צמצום אזור עבודה (AppBar)", true, "דולג לבקשת המשתמש");
        }

        RunCursorClampCheck(target);
        RunHotKeyCheck();
        RunDiagnosticsCheck(displays, target);

        var exitCode = Report();
        Write($"הפלט נשמר בקובץ: {_outputPath}");
        FlushTranscript();
        return exitCode;
    }

    /// <summary>
    /// מאמת שכל מבנה אינטראפ נטען בגודל הנכון. גודל שגוי אינו זורק חריגה —
    /// הוא פשוט מחזיר כשל שקט מהמערכת, ולכן זו הבדיקה הראשונה שצריכה לרוץ.
    /// </summary>
    private static void RunStructSizeCheck()
    {
        var checks = DisplayProbe.StructSizeChecks();
        var failures = checks.Where(c => c.Actual != c.Expected).ToList();

        Write("גדלי מבני האינטראפ:");
        foreach (var (name, actual, expected) in checks)
        {
            Write($"  {(actual == expected ? "תקין" : "שגוי")}  {name}: {actual} (מצופה {expected})");
        }

        Write();

        Record(
            "גדלי מבני האינטראפ",
            failures.Count == 0,
            failures.Count == 0
                ? $"כל {checks.Count} המבנים בגודל הנכון"
                : "שגוי: " + string.Join(", ", failures.Select(f => $"{f.Name}={f.Actual} במקום {f.Expected}")));
    }

    private static List<DisplayInfo> RunDisplayCheck()
    {
        var provider = new WindowsDisplayProvider();
        var displays = provider.GetDisplays().ToList();

        Write($"זוהו {displays.Count} מסכים:");
        foreach (var display in displays)
        {
            Write($"  • {display.Describe()}");
            Write($"      מזהה: {display.DeviceName} | אזור עבודה: {display.WorkArea}");
            Write($"      EDID: {(string.IsNullOrEmpty(display.EdidHash) ? "לא זוהה" : display.EdidHash)} | " +
                              $"גודל פיזי: {(display.PhysicalWidthMm is null ? "לא ידוע" : display.PhysicalWidthMm + " מ״מ")}");
        }

        Write();
        Record("מיפוי מסכים", displays.Count > 0, $"{displays.Count} מסכים");
        return displays;
    }

    private static void RunGeometryCheck(DisplayInfo target)
    {
        // בדיקה מול הנתונים האמיתיים של המסך, עם שוליים לא-סימטריים.
        var margins = VisibleAreaCalculator.Clamp(target.Bounds, new MarginSet(38, 64, 22, 14));

        var visible = VisibleAreaCalculator.Compute(target.Bounds, margins);
        var valid = VisibleAreaCalculator.Validate(target.Bounds, margins).IsValid;

        var expectedWidth = target.Bounds.Width - margins.TotalHorizontal;
        var expectedHeight = target.Bounds.Height - margins.TotalVertical;

        var geometryOk = valid &&
                         visible.Width == expectedWidth &&
                         visible.Height == expectedHeight &&
                         visible.X == target.Bounds.X + margins.Left &&
                         visible.Y == target.Bounds.Y + margins.Top;

        Record(
            "חישוב האזור הגלוי",
            geometryOk,
            $"{target.Bounds} פחות {margins} = {visible}");

        // בדיקת אי-סימטריות: שני צדדים שונים בכל ציר.
        var asymmetric = margins.IsAsymmetric;
        var symmetricEquivalent = margins.ToSymmetric();

        Record(
            "זיהוי שוליים לא-סימטריים",
            asymmetric,
            $"שוליים סימטריים חלופיים היו {symmetricEquivalent} (מאבדים פיקסלים גלויים)");
    }

    private static void RunOverlayCheck(DisplayInfo target, bool flash)
    {
        try
        {
            var margins = VisibleAreaCalculator.Clamp(target.Bounds, new MarginSet(48, 72, 30, 18));

            using var overlay = new OverlayHost();

            // הבדיקה חייבת להציג את השכבה בפועל: רק כך אפשר לקרוא חזרה את המלבנים
            // ולאמת שהגיאומטריה הוחלה ולא רק חושבת. --no-flash מקצר את ההצגה בלבד.
            Write(flash
                ? "מציג את שכבת הכיסוי על המסך לשתי שניות, כדי לוודא שהיא מופיעה ומוקמת נכון..."
                : "מציג את שכבת הכיסוי ל-150 מ״ש בלבד (מצב --no-flash)...");

            overlay.Apply(target.Bounds, margins, OverlayStyle.Preview, visible: true);
            Thread.Sleep(flash ? 2000 : 150);

            var strips = overlay.GetStripRects();
            var expected = ExpectedStrips(target.Bounds, margins);

            var ok = strips.Count == expected.Count;
            var detail = new System.Text.StringBuilder();

            foreach (var (side, rect) in expected)
            {
                var actual = strips.FirstOrDefault(s => s.Side == side);
                var match = actual != default && actual.Rect == rect;
                ok &= match;

                detail.Append($"{side}: {(match ? "תקין" : $"בפועל {actual.Rect} במקום {rect}")}; ");
            }

            overlay.Hide();
            Thread.Sleep(200);

            Record("שכבת כיסוי על השוליים", ok, ok ? "ארבע הרצועות מוקמו בדיוק" : detail.ToString());
        }
        catch (Exception ex)
        {
            Record("שכבת כיסוי על השוליים", false, $"חריגה: {ex.Message}");
        }
    }

    private static Dictionary<MarginSide, PixelRect> ExpectedStrips(PixelRect monitor, MarginSet margins)
    {
        var visibleWidth = monitor.Width - margins.TotalHorizontal;

        return new Dictionary<MarginSide, PixelRect>
        {
            [MarginSide.Left] = new(monitor.X, monitor.Y, margins.Left, monitor.Height),
            [MarginSide.Right] = new(monitor.Right - margins.Right, monitor.Y, margins.Right, monitor.Height),
            [MarginSide.Top] = new(monitor.X + margins.Left, monitor.Y, visibleWidth, margins.Top),
            [MarginSide.Bottom] = new(monitor.X + margins.Left, monitor.Bottom - margins.Bottom, visibleWidth, margins.Bottom)
        };
    }

    private static void RunWorkAreaCheck(DisplayInfo target)
    {
        var margins = VisibleAreaCalculator.Clamp(target.Bounds, new MarginSet(40, 60, 24, 16));
        var before = DiagnosticsService.GetWorkArea(target);

        using var workArea = new WorkAreaHost();

        Write();
        Write("בודק את צמצום אזור העבודה (AppBar). אזור העבודה של Windows ישתנה לשנייה ויחזור...");

        if (!workArea.TryApply(target.Bounds, margins, out var message))
        {
            Record("צמצום אזור עבודה (AppBar)", false, message);
            return;
        }

        Thread.Sleep(900);

        var during = DiagnosticsService.GetWorkArea(target);
        var matches = during is not null && WorkAreaHost.WorkAreaRespectsMargins(target.Bounds, during.Value, margins);
        var shrank = during is not null && before is not null &&
                     (during.Value.Width < before.Value.Width || during.Value.Height < before.Value.Height);

        workArea.Revert();
        Thread.Sleep(500);

        var after = DiagnosticsService.GetWorkArea(target);

        Write($"  לפני:  {Describe(before)}");
        Write($"  בזמן:  {Describe(during)}");
        Write($"  אחרי:  {Describe(after)}");
        Write($"  מצופה: {VisibleAreaCalculator.Compute(target.Bounds, margins)}");
        Write();

        var reverted = before is null || after is null ||
                       (Math.Abs(after.Value.Width - before.Value.Width) <= 4 &&
                        Math.Abs(after.Value.Height - before.Value.Height) <= 4);

        Record(
            "צמצום אזור עבודה (AppBar)",
            matches,
            matches
                ? $"אזור העבודה מצומצם לתוך האזור הגלוי{(shrank ? " (הצטמצם בפועל)" : string.Empty)} — מקסם, Snap וסרגל המשימות יכבדו את השוליים"
                : "אזור העבודה חורג אל תוך השוליים — הנפילה החלופית (התאמת חלונות) נדרשת");

        Record("החזרת אזור העבודה למצבו", reverted, reverted ? "המצב שוחזר" : "אזור העבודה לא חזר למצבו המקורי");
    }

    private static string Describe(PixelRect? rect) => rect is null ? "לא ידוע" : rect.Value.ToString();

    private static void RunCursorClampCheck(DisplayInfo target)
    {
        var visible = VisibleAreaCalculator.Compute(
            target.Bounds,
            VisibleAreaCalculator.Clamp(target.Bounds, new MarginSet(48, 72, 30, 18)));

        try
        {
            var applied = CursorClamp.Apply(visible);
            var current = CursorClamp.GetCurrent();
            var matches = current is not null && current.Value.Width == visible.Width && current.Value.Height == visible.Height;

            CursorClamp.Release();
            var released = CursorClamp.IsReleased();

            Record(
                "חסימת סמן העכבר",
                applied && matches && released,
                $"הוחל {visible}, נקרא בחזרה {Describe(current)}, שוחרר: {(released ? "כן" : "לא")}");
        }
        catch (Exception ex)
        {
            CursorClamp.Release();
            Record("חסימת סמן העכבר", false, $"חריגה: {ex.Message}");
        }
    }

    private static void RunHotKeyCheck()
    {
        try
        {
            var registered = GlobalHotKey.Probe(
                GlobalHotKey.PanicModifiers,
                GlobalHotKey.PanicVirtualKey,
                out var error);

            Record(
                "רישום מקש חירום גלובלי (Ctrl+Alt+Shift+F12)",
                registered,
                registered ? "נרשם ושוחרר בהצלחה" : $"הרישום נכשל (קוד {error}) — ייתכן שהצירוף תפוס");
        }
        catch (Exception ex)
        {
            Record("רישום מקש חירום גלובלי (Ctrl+Alt+Shift+F12)", false, $"חריגה: {ex.Message}");
        }
    }

    private static void RunDiagnosticsCheck(IReadOnlyList<DisplayInfo> displays, DisplayInfo target)
    {
        try
        {
            var report = DiagnosticsService.BuildReport(
                displays,
                target,
                new MarginSet(38, 64, 22, 14),
                overlayActive: false,
                workAreaActive: false,
                cursorClamped: false,
                watcherActive: false,
                refitCount: 0,
                uncooperativeCount: 0);

            var adapters = DiagnosticsService.GetAdapterNames();
            var ok = report.Length > 0 && adapters.Count > 0;

            Write("כרטיסי מסך שזוהו:");
            foreach (var adapter in adapters)
            {
                Write($"  • {adapter}");
            }

            Write();

            Record(
                "בניית דוח אבחון",
                ok,
                $"{adapters.Count} כרטיסי מסך, דוח באורך {report.Length} תווים");
        }
        catch (Exception ex)
        {
            Record("בניית דוח אבחון", false, $"חריגה: {ex.Message}");
        }
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static void Record(string name, bool passed, string detail) =>
        Results.Add((name, passed, detail));

    private static int Report()
    {
        Write("=== תוצאות ===");

        var failed = 0;
        foreach (var (name, passed, detail) in Results)
        {
            Write($"  [{(passed ? "עבר" : "כשל")}] {name}");
            if (!string.IsNullOrWhiteSpace(detail))
            {
                Write($"         {detail}");
            }

            if (!passed)
            {
                failed++;
            }
        }

        Write();
        Write(failed == 0
            ? $"כל {Results.Count} הבדיקות עברו."
            : $"{Results.Count - failed} מתוך {Results.Count} עברו; {failed} נכשלו.");
        Write();

        return failed == 0 ? 0 : 2;
    }
}
