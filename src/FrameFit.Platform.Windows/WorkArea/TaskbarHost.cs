using System.Runtime.InteropServices;
using System.Text;
using FrameFit.Core;
using FrameFit.Core.Geometry;
using FrameFit.Platform.Windows.Interop;

namespace FrameFit.Platform.Windows.WorkArea;

/// <summary>
/// סרגל המשימות של Windows.
///
/// סרגל המשימות הוא חלון של ה-Shell שמוצמד לשולי המסך, והוא מקצה לעצמו מקום באזור
/// העבודה בכל מצב — גם כשהשוליים שמאחוריו מוסתרים מאחורי מסגרת פיזית. הוא אינו נעלם
/// מעצמו, ולכן: (א) בלי קיזוז הוא גוזל חלק מהאזור הגלוי, ו-(ב) הוא ממשיך להיות מצויר
/// מעל השוליים שכבר הוגדרו כמוסתרים ושחורים.
///
/// התפקיד של המחלקה הזו: לאתר את סרגל המשימות של המסך המנוהל, להסתיר אותו כל עוד
/// FrameFit פעיל, ולהחזיר אותו בכל מסלול יציאה — גם אם התהליך נהרג.
/// </summary>
public sealed class TaskbarHost : IDisposable
{
    private const string PrimaryClass = "Shell_TrayWnd";
    private const string SecondaryClass = "Shell_SecondaryTrayWnd";

    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

    /// <summary>כמה זמן ממתינים לייצוב מיקום הסרגל לפני שבודקים אם ההעברה באמת החזיקה.</summary>
    private static readonly TimeSpan PositionVerifyTimeout = TimeSpan.FromMilliseconds(350);
    private static readonly Native.EnumWindowsProc StaticEnumProc = OnStaticEnum;
    private static readonly List<IntPtr> StaticFound = new();

    private readonly Action<string> _log;
    private readonly Native.EnumWindowsProc _enumCallback;
    private readonly object _sync = new();

    private IntPtr _candidate;
    private long _candidateArea;
    private PixelRect _candidateRect;

    private IntPtr _handle;
    private PixelRect _rect;
    private PixelRect _monitor;
    private PixelRect? _naturalRect;
    private PixelRect? _moveTarget;
    private Timer? _watchdog;
    private bool _hiddenByUs;
    private bool _movedByUs;
    private bool _disposed;

    public TaskbarHost(Action<string>? log = null)
    {
        _log = log ?? (_ => { });
        _enumCallback = OnEnumWindow;
    }

    /// <summary>האם נמצא סרגל משימות במסך המנוהל.</summary>
    public bool IsAttached => _handle != IntPtr.Zero;

    /// <summary>האם אנחנו אלה שהסתירו אותו כרגע.</summary>
    public bool IsHiddenByUs => _hiddenByUs;

    /// <summary>האם אנחנו אלה שהעברנו אותו אל תוך האזור הגלוי.</summary>
    public bool IsMovedByUs => _movedByUs;

    /// <summary>המלבן שאליו הועבר הסרגל, אם הועבר.</summary>
    public PixelRect? MoveTarget => _moveTarget;

    /// <summary>האם אנחנו מטפלים בסרגל כרגע (מוסתר או מועבר).</summary>
    public bool IsHandledByUs => _hiddenByUs || _movedByUs;

    /// <summary>האם החלון מוצג כרגע בפועל.</summary>
    public bool IsVisible => IsAttached && Native.IsWindow(_handle) && Native.IsWindowVisible(_handle);

    /// <summary>המלבן של סרגל המשימות בזמן האיתור.</summary>
    public PixelRect Rect => _rect;

    public string WindowClass { get; private set; } = string.Empty;

    /// <summary>
    /// מאתר את סרגל המשימות של המסך הנתון. במסך משני שאין בו סרגל משימות —
    /// מחזיר false, וזה מצב תקין לחלוטין.
    /// </summary>
    public bool TryAttach(PixelRect monitor, out string message)
    {
        _monitor = monitor;
        lock (_sync)
        {
            _candidate = IntPtr.Zero;
            _candidateArea = 0;

            Native.EnumWindows(_enumCallback, IntPtr.Zero);
        }

        if (_candidate == IntPtr.Zero)
        {
            message = $"לא נמצא סרגל משימות במסך {monitor} — אין מה להסתיר.";
            return false;
        }

        _handle = _candidate;
        _rect = _candidateRect;
        WindowClass = GetClassName(_handle);

        message = $"סרגל המשימות זוהה במסך המנוהל: {_rect} ({WindowClass}).";
        return true;
    }

    /// <summary>החלק מסרגל המשימות שגולש אל תוך האזור הגלוי (אם בכלל).</summary>
    public PixelRect OverlapWithVisible(PixelRect monitor, MarginSet margins) =>
        _rect.Intersect(VisibleAreaCalculator.Compute(monitor, margins));

    /// <summary>האם סרגל המשימות נמצא כולו בתוך האזור המוסתר — כלומר אינו נראה לכאורה.</summary>
    public bool IsEntirelyInHiddenArea(PixelRect monitor, MarginSet margins) =>
        OverlapWithVisible(monitor, margins).IsEmpty;

    /// <summary>מסתיר את סרגל המשימות ומתחיל לשמור על המצב עד לשחזור.</summary>
    public bool Hide(out string message)
    {
        if (_disposed)
        {
            message = "האובייקט שוחרר.";
            return false;
        }

        if (!IsAttached || !Native.IsWindow(_handle))
        {
            message = "סרגל המשימות לא נמצא — לא הוסתר.";
            return false;
        }

        if (!Native.IsWindowVisible(_handle))
        {
            // מישהו אחר כבר הסתיר אותו; לא ננכס לעצמנו את האחריות להחזירו.
            message = "סרגל המשימות כבר מוסתר — לא נגענו בו.";
            return true;
        }

        // מצב קודם של הזזה מבוטל, כדי שהשחזור יהיה למקום אחד וברור.
        UndoMove();

        SetVisible(_handle, false);
        _hiddenByUs = Native.IsWindowVisible(_handle) == false;

        if (_hiddenByUs)
        {
            WriteMarker();
            StartWatchdog();
            _log($"סרגל המשימות הוסתר ({_rect}). הוא יוחזר ביציאה מהתוכנה או בלחיצה על מקש המילוט.");
        }

        message = _hiddenByUs
            ? "סרגל המשימות הוסתר."
            : "הסתרת סרגל המשימות נכשלה.";
        return _hiddenByUs;
    }

    /// <summary>
    /// מנסה להעביר את סרגל המשימות אל תוך האזור הגלוי ולעגן אותו בשוליו התחתונים.
    ///
    /// <b>מגבלה שנמדדה:</b> ב-Windows 11 ה-Shell מחזיק את הסרגל בקצה המסך שלו ומחזיר אותו
    /// לשם **מיד** אחרי כל הזזה (נמדד: 300 מתוך 300 דגימות, וגם בניסיון הזזה שני בתוך אותה
    /// אלפית שנייה). לכן אין להסתפק בכך ש-`SetWindowPos` הצליח — הוא מחזיר הצלחה גם כשהסרגל
    /// חוזר מיד לאחר מכן. הפעולה הזו מאמתת את המיקום **בפועל**, ואם ה-Shell ניצח: הסרגל
    /// מוחזר למקומו, המצב הפנימי מנוקה, ואין גוזלים מהאזור הגלוי רצועה שאינה בשימוש.
    /// </summary>
    public bool MoveInto(PixelRect visible, out string message)
    {
        if (_disposed)
        {
            message = "האובייקט שוחרר.";
            return false;
        }

        if (!IsAttached || !Native.IsWindow(_handle))
        {
            message = "סרגל המשימות לא נמצא — לא הועבר.";
            return false;
        }

        if (visible.IsEmpty || _rect.Height > visible.Height)
        {
            message = "האזור הגלוי קטן מגובה סרגל המשימות — הסרגל לא הועבר.";
            return false;
        }

        // מצב קודם של הסתרה מבוטל, כדי שהסרגל יהיה גלוי כשמעבירים אותו.
        if (_hiddenByUs)
        {
            SetVisible(_handle, true);
            _hiddenByUs = false;
            DeleteMarker();
        }

        // המקום הטבעי שאליו מחזירים בשחזור — שורת התחתית של המסך.
        _naturalRect = new PixelRect(_monitor.X, _monitor.Bottom - _rect.Height, _monitor.Width, _rect.Height);

        var target = new PixelRect(visible.X, visible.Bottom - _rect.Height, visible.Width, _rect.Height);
        if (!ApplyPosition(target, out var error))
        {
            message = error;
            return false;
        }

        if (!VerifyPosition(target, out var actual))
        {
            // ה-Shell החזיר את הסרגל לקצה המסך. מחזירים גם את ההצהרה על מיקומו, כדי שלא
            // תישאר רשומת סרגל יישום במקום שאינו בשימוש.
            ApplyPosition(_naturalRect.Value, out _);

            message = $"Windows לא מאפשר להזיז את סרגל המשימות: הוא הוחזר אל {Describe(actual)} " +
                      $"במקום {target}. הסרגל נשאר במקומו, ולא נגרע דבר מהאזור הגלוי.";
            _log($"אזהרה: {message}");
            return false;
        }

        _moveTarget = target;
        _movedByUs = true;
        StartWatchdog();

        _log($"סרגל המשימות עוגן בתחתית האזור הגלוי ({target}). הוא יוחזר ביציאה מהתוכנה או במקש המילוט.");
        message = $"סרגל המשימות עוגן בתחתית האזור הגלוי ({target}).";
        return true;
    }

    /// <summary>
    /// מאמת שהסרגל באמת נמצא ביעד. ה-Shell עשוי להחזיר אותו מיד, ו-`SetWindowPos`
    /// מחזיר הצלחה גם אז — ולכן הקריאה חוזרת היא מקור האמת.
    /// </summary>
    private bool VerifyPosition(PixelRect target, out PixelRect actual)
    {
        var deadline = Environment.TickCount64 + (long)PositionVerifyTimeout.TotalMilliseconds;

        while (true)
        {
            actual = ReadCurrentRect() ?? default;

            if (actual == target)
            {
                return true;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            Thread.Sleep(25);
        }
    }

    private static string Describe(PixelRect rect) =>
        rect.IsEmpty ? "מיקום לא ידוע" : rect.ToString();

    /// <summary>
    /// מוודא שהסרגל נשאר במקום שאליו העברנו אותו. ה-Shell נוהג להחזיר את הסרגל לעצמו
    /// באירועים שונים, ולכן זו אינה פעולה חד-פעמית. בטוח לקריאה חוזרת.
    /// </summary>
    public void EnsurePosition()
    {
        lock (_sync)
        {
            if (_disposed || !_movedByUs || _moveTarget is not { } target)
            {
                return;
            }

            if (!Native.IsWindow(_handle) && !TryAttach(_monitor, out _))
            {
                return;
            }

            if (!Native.IsWindowVisible(_handle))
            {
                // ה-Shell מסתיר את הסרגל בעצמו כשחלון נכנס למסך מלא. זו הסתרה מכוונת —
                // לא מחזירים אותו בכוח, אחרת סרגל on-top היה מופיע מעל היישום במסך מלא.
                return;
            }

            if (!Native.GetWindowRect(_handle, out var rect))
            {
                return;
            }

            var current = new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
            if (current == target)
            {
                return;
            }

            // אם ה-Shell הופעל מחדש, גובה הסרגל עשוי להשתנות — מתאימים את היעד.
            if (current.Height != target.Height)
            {
                target = target with { Y = target.Bottom - current.Height, Height = current.Height };
            }

            if (ApplyPosition(target, out _))
            {
                _moveTarget = target;
            }
        }
    }

    /// <summary>
    /// מחזיר את סרגל המשימות למצבו. בטוח לקריאה חוזרת.
    ///
    /// המצב הפנימי מנוקה **לפני** הפעולות על הסרגל: פעימת השומר שרצה ברקע בזמן
    /// השחזור הייתה אחרת רואה "עדיין מועבר", מחזירה את הסרגל למקום המעוגן מיד אחרי
    /// שהחזרנו אותו, ומשאירה אותו שם. זו תקלה שנצפתה בבדיקה העצמית של הקובץ המותקן.
    /// </summary>
    public void Restore()
    {
        StopWatchdog();

        bool wasHidden;
        bool wasMoved;
        PixelRect? natural;
        IntPtr handle;

        lock (_sync)
        {
            wasHidden = _hiddenByUs;
            wasMoved = _movedByUs;
            natural = _naturalRect;
            handle = _handle;

            _hiddenByUs = false;
            _movedByUs = false;
            _moveTarget = null;
        }

        if (handle != IntPtr.Zero && Native.IsWindow(handle))
        {
            if (wasHidden)
            {
                SetVisible(handle, true);
                _log($"סרגל המשימות הוחזר ({_rect}).");
            }
            else if (wasMoved && natural is { } rect)
            {
                if (ApplyPosition(rect, out var error))
                {
                    _log($"סרגל המשימות הוחזר למקומו ({rect}).");
                }
                else
                {
                    _log($"אזהרה: {error} הסרגל נקרא חזרה במיקום " +
                         $"{ReadCurrentRect()?.ToString() ?? "לא ידוע"} במקום {rect}.");
                }
            }
        }

        DeleteMarker();
    }

    /// <summary>
    /// קורא את מלבן הסרגל ישירות ממערכת ההפעלה. השמירה הפנימית עשויה להיות מיושנת
    /// (למשל אם ה-Shell הזיז אותו בעצמו), ולכן אימותים צריכים לקרוא מכאן.
    /// </summary>
    public PixelRect? ReadCurrentRect() =>
        _handle != IntPtr.Zero && Native.IsWindow(_handle) && Native.GetWindowRect(_handle, out var rect)
            ? new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height)
            : null;

    /// <summary>
    /// מזיז את סרגל המשימות בפועל. שתי פעולות, כי ה-Shell אינו עקבי: הצהרת מיקום דרך
    /// מנגנון סרגלי היישום (כך ש-Windows יגזור ממנה גם את אזור העבודה), והזזה של החלון
    /// עצמו — זו שאמורה לקרות מיד ובאופן מורגש.
    /// </summary>
    /// <summary>מחזיר את הסרגל למקומו הטבעי ומבטל את מצב ההזזה, בלי לגעת בהסתרה.</summary>
    private void UndoMove()
    {
        if (!_movedByUs)
        {
            return;
        }

        if (_handle != IntPtr.Zero && Native.IsWindow(_handle) && _naturalRect is { } natural)
        {
            ApplyPosition(natural, out _);
        }

        _movedByUs = false;
        _moveTarget = null;
    }

    private bool ApplyPosition(PixelRect rect, out string error)
    {
        var data = new Native.APPBARDATA
        {
            cbSize = Marshal.SizeOf<Native.APPBARDATA>(),
            hWnd = _handle,
            uEdge = Native.ABE_BOTTOM,
            rc = new Native.RECT(rect.X, rect.Y, rect.Right, rect.Bottom)
        };

        Native.SHAppBarMessage(Native.ABM_SETPOS, ref data);

        var moved = Native.SetWindowPos(
            _handle,
            Native.HWND_TOPMOST,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        if (moved)
        {
            _rect = rect;
            error = string.Empty;
        }
        else
        {
            error = "הזזת סרגל המשימות נכשלה (SetWindowPos).";
        }

        return moved;
    }

    /// <summary>
    /// מחזיר סרגל משימות שהוסתר ולא שוחזר — למשל אם התהליך נהרג בעודו מוסתר.
    /// נקרא בעליית התוכנה, לפני כל פעולה אחרת.
    /// </summary>
    public static bool TryRecoverAbandoned(out string message)
    {
        message = string.Empty;

        try
        {
            if (!File.Exists(AppPaths.TaskbarMarkerFile))
            {
                return false;
            }

            StaticFound.Clear();
            Native.EnumWindows(StaticEnumProc, IntPtr.Zero);

            var recovered = 0;
            foreach (var handle in StaticFound)
            {
                if (!Native.IsWindowVisible(handle))
                {
                    SetVisible(handle, true);
                    recovered++;
                }
            }

            File.Delete(AppPaths.TaskbarMarkerFile);

            message = recovered > 0
                ? $"הוחזר סרגל משימות שהוסתר ולא שוחזר ({recovered})."
                : "סימון של סרגל משימות מוסתר נוקה.";
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>תיאור המצב לדוח האבחון.</summary>
    public string Describe(PixelRect monitor, MarginSet margins)
    {
        if (!IsAttached)
        {
            return "לא נמצא סרגל משימות במסך המנוהל";
        }

        var state = IsHiddenByUs
            ? "הוסתר ע\"י FrameFit"
            : IsMovedByUs
                ? "עוגן בתוך האזור הגלוי"
                : IsVisible ? "גלוי (במקומו הטבעי)" : "מוסתר";

        var where = IsEntirelyInHiddenArea(monitor, margins)
            ? "כולו באזור המוסתר"
            : $"בתוך האזור הגלוי: {OverlapWithVisible(monitor, margins)}";

        return $"{_rect} ({WindowClass}) — {state}; {where}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Restore();
    }

    private void StartWatchdog()
    {
        _watchdog ??= new Timer(_ => OnWatchdog(), null, WatchdogInterval, WatchdogInterval);
    }

    private void StopWatchdog()
    {
        _watchdog?.Dispose();
        _watchdog = null;
    }

    /// <summary>
    /// ה-Shell מחזיר את סרגל המשימות לעצמו באירועים שונים (שינוי הגדרות, הפעלה של מסך
    /// מלא וכו'). הפעימה הזו מחזירה אותו למצב שהגדרנו, בלי להתערב כשאין צורך.
    /// </summary>
    private void OnWatchdog()
    {
        lock (_sync)
        {
            if (_disposed || !IsHandledByUs)
            {
                return;
            }

            if (!Native.IsWindow(_handle))
            {
                // ה-Shell הופעל מחדש — מחפשים את סרגל המשימות החדש באותו מסך.
                if (!TryAttach(_monitor, out _))
                {
                    return;
                }
            }

            if (_hiddenByUs && Native.IsWindowVisible(_handle))
            {
                SetVisible(_handle, false);
            }

            if (_movedByUs)
            {
                EnsurePosition();
            }
        }
    }

    private bool OnEnumWindow(IntPtr hwnd, IntPtr lParam)
    {
        var className = GetClassName(hwnd);
        if (!IsTaskbarClass(className))
        {
            return true;
        }

        if (!Native.GetWindowRect(hwnd, out var rect))
        {
            return true;
        }

        var windowRect = new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
        var area = windowRect.Intersect(_monitor).Area;

        if (area > _candidateArea)
        {
            _candidateArea = area;
            _candidate = hwnd;
            _candidateRect = windowRect;
        }

        return true;
    }

    private static bool OnStaticEnum(IntPtr hwnd, IntPtr lParam)
    {
        if (IsTaskbarClass(GetClassName(hwnd)))
        {
            StaticFound.Add(hwnd);
        }

        return true;
    }

    private static bool IsTaskbarClass(string className) =>
        className.Equals(PrimaryClass, StringComparison.Ordinal) ||
        className.Equals(SecondaryClass, StringComparison.Ordinal);

    private static void SetVisible(IntPtr hwnd, bool visible)
    {
        Native.ShowWindow(hwnd, visible ? Native.SW_SHOWNA : Native.SW_HIDE);

        if (Native.IsWindowVisible(hwnd) == visible)
        {
            return;
        }

        // גיבוי: לחלון שלא הגיב ל-ShowWindow משלימים בהצגה/הסתרה דרך SetWindowPos.
        Native.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE |
            (visible ? Native.SWP_SHOWWINDOW : Native.SWP_HIDEWINDOW));
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        var length = Native.GetClassName(hwnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString() : string.Empty;
    }

    private static void WriteMarker()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.TaskbarMarkerFile)!);
            File.WriteAllText(AppPaths.TaskbarMarkerFile, DateTime.Now.ToString("O"));
        }
        catch (Exception)
        {
            // הסימון הוא רשת ביטחון בלבד; אי אפשר להפיל בגללו את ההסתרה.
        }
    }

    private static void DeleteMarker()
    {
        try
        {
            if (File.Exists(AppPaths.TaskbarMarkerFile))
            {
                File.Delete(AppPaths.TaskbarMarkerFile);
            }
        }
        catch (Exception)
        {
            // אין מה לעשות — הסימון ינוקה בעלייה הבאה.
        }
    }
}
