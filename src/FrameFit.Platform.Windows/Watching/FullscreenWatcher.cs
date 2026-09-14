using System.Runtime.InteropServices;
using System.Text;
using FrameFit.Core.Geometry;
using FrameFit.Platform.Windows.Interop;

namespace FrameFit.Platform.Windows.Watching;

/// <summary>
/// שכבה 2 — ליבת המוצר. התוכן שנמסר (דפדפן ב-F11, מצב הצגה של מצגות, שקופיות תמונות,
/// נגני מדיה) נכנס ל"מסך מלא" לפי כל מלבן המסך, ולא לפי אזור העבודה. לכן יש לזהות
/// חלון כזה ולהתאים אותו לאזור הגלוי.
///
/// היסטרזיס: אם תוכנית מחזירה את עצמה למסך-מלא שוב ושוב, מפסיקים להילחם בה ומדווחים.
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    /// <summary>חלק המסך שחלון חייב לכסות כדי להיחשב "מסך-מלא" (מזהה גם חלון ממוקסם).</summary>
    private const double FullscreenCoverageThreshold = 0.92;

    private const int MaxAttemptsPerWindow = 3;

    private static readonly TimeSpan AttemptWindow = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan FastTick = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan IdleTick = TimeSpan.FromSeconds(2);

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "Button",
        "FrameFitOverlayWindow",
        "FrameFitMessageWindow",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
        "TaskListThumbnailWnd",
        "ForegroundStaging",
        "MsgrWindow",
        "SysShadow"
    };

    private readonly Action<string> _log;
    private readonly object _sync = new();
    private readonly Dictionary<IntPtr, Attempt> _attempts = new();
    private readonly HashSet<IntPtr> _uncooperative = new();
    private readonly Native.WinEventDelegate _winEventCallback;
    private readonly Native.EnumWindowsProc _enumWindowsCallback;

    private Timer? _timer;
    private IntPtr _locationHook;
    private IntPtr _foregroundHook;
    private PixelRect _monitor;
    private MarginSet _margins = MarginSet.Empty;
    private bool _pendingRefit;
    private bool _running;
    private bool _handleMaximizedWindows;
    private bool _disposed;

    public FullscreenWatcher(PixelRect monitor, MarginSet margins, bool handleMaximizedWindows, Action<string> log)
    {
        _monitor = monitor;
        _margins = margins;
        _handleMaximizedWindows = handleMaximizedWindows;
        _log = log;
        _winEventCallback = OnWinEvent;
        _enumWindowsCallback = OnEnumWindow;
    }

    /// <summary>מספר החלונות שהותאמו מאז ההפעלה — לדוח האבחון.</summary>
    public int RefitCount { get; private set; }

    /// <summary>תוכניות שמזוהות כמתנגדות לאכיפה — מדווח באבחון ולא מנוטרלות בכוח.</summary>
    public IReadOnlyCollection<IntPtr> UncooperativeWindows
    {
        get
        {
            lock (_sync)
            {
                return _uncooperative.ToArray();
            }
        }
    }

    public void Update(PixelRect monitor, MarginSet margins, bool handleMaximizedWindows)
    {
        lock (_sync)
        {
            _monitor = monitor;
            _margins = margins;
            _handleMaximizedWindows = handleMaximizedWindows;
        }

        _pendingRefit = true;
    }

    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        _running = true;

        _locationHook = Native.SetWinEventHook(
            Native.EVENT_OBJECT_LOCATIONCHANGE,
            Native.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _winEventCallback,
            0,
            0,
            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

        _foregroundHook = Native.SetWinEventHook(
            Native.EVENT_SYSTEM_FOREGROUND,
            Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventCallback,
            0,
            0,
            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

        _timer = new Timer(_ => Tick(), null, FastTick, FastTick);
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;

        _timer?.Dispose();
        _timer = null;

        if (_locationHook != IntPtr.Zero)
        {
            Native.UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }

        if (_foregroundHook != IntPtr.Zero)
        {
            Native.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint thread,
        uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != Native.OBJID_WINDOW)
        {
            return;
        }

        _pendingRefit = true;
    }

    private void Tick()
    {
        if (!_running)
        {
            return;
        }

        var shouldRun = _pendingRefit;
        _pendingRefit = false;

        if (!shouldRun)
        {
            // פעימת ביטחון נדירה כדי לתפוס מקרים שפספסו את ה-hooks.
            var tickCount = Environment.TickCount64;
            shouldRun = tickCount % (long)(IdleTick.TotalMilliseconds * 2) < FastTick.TotalMilliseconds;
        }

        if (shouldRun)
        {
            RunReconcile();
        }
    }

    private void RunReconcile()
    {
        try
        {
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                PruneAttempts(now);
            }

            Native.EnumWindows(_enumWindowsCallback, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _log($"שגיאה בהתאמת חלונות: {ex.Message}");
        }
    }

    private void PruneAttempts(DateTime now)
    {
        var stale = _attempts
            .Where(pair => now - pair.Value.FirstSeen > AttemptWindow * 2)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in stale)
        {
            _attempts.Remove(key);
        }
    }

    private bool OnEnumWindow(IntPtr hwnd, IntPtr lParam)
    {
        try
        {
            if (ShouldConsider(hwnd, out var windowRect, out var isMaximized))
            {
                Refit(hwnd, windowRect, isMaximized);
            }
        }
        catch (Exception)
        {
            // חלון בודד לא אמור להפיל את הסריקה.
        }

        return true;
    }

    private bool ShouldConsider(IntPtr hwnd, out PixelRect windowRect, out bool isMaximized)
    {
        windowRect = default;
        isMaximized = false;

        PixelRect monitor;
        MarginSet margins;
        bool handleMaximized;

        lock (_sync)
        {
            monitor = _monitor;
            margins = _margins;
            handleMaximized = _handleMaximizedWindows;

            if (_uncooperative.Contains(hwnd))
            {
                return false;
            }
        }

        if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
        {
            return false;
        }

        var className = GetClassName(hwnd);
        if (string.IsNullOrEmpty(className) || IgnoredClasses.Contains(className))
        {
            return false;
        }

        var exStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        if ((exStyle & Native.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        Native.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == (uint)Environment.ProcessId)
        {
            return false;
        }

        if (!Native.GetWindowRect(hwnd, out var nativeRect))
        {
            return false;
        }

        windowRect = new PixelRect(
            nativeRect.Left,
            nativeRect.Top,
            nativeRect.Width,
            nativeRect.Height);

        if (windowRect.CoverageOf(monitor) < FullscreenCoverageThreshold)
        {
            return false;
        }

        var visibleArea = VisibleAreaCalculator.Compute(monitor, margins);
        if (windowRect.IsWithin(visibleArea))
        {
            // כבר מתאים — אין מה לעשות.
            return false;
        }

        isMaximized = Native.IsZoomed(hwnd);

        // חלון ממוקסם מטופל בשכבה 1 (אזור העבודה). רק אם היא אינה זמינה,
        // ומבקשים זאת במפורש, מטפלים בו כאן.
        if (isMaximized && !handleMaximized)
        {
            return false;
        }

        return true;
    }

    private void Refit(IntPtr hwnd, PixelRect windowRect, bool isMaximized)
    {
        PixelRect visibleArea;

        lock (_sync)
        {
            visibleArea = VisibleAreaCalculator.Compute(_monitor, _margins);
        }

        if (!TrackAttempt(hwnd))
        {
            return;
        }

        if (isMaximized)
        {
            // אין דרך להזיז חלון ממוקסם בלי לבטל את המקסם; מחזירים למצב רגיל ואז ממקמים.
            Native.ShowWindow(hwnd, Native.SW_RESTORE);
        }

        Native.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            visibleArea.X,
            visibleArea.Y,
            visibleArea.Width,
            visibleArea.Height,
            Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);

        RefitCount++;
        _log($"הותאם חלון 0x{hwnd.ToInt64():X} מ-{windowRect} אל {visibleArea}{(isMaximized ? " (ממוקסם)" : string.Empty)}");
    }

    /// <summary>
    /// מונע לולאת מאבק: אם אותה תוכנית חוזרת למסך-מלא יותר מדי פעמים בחלון זמן קצר,
    /// מפסיקים להתערב ומדווחים עליה.
    /// </summary>
    private bool TrackAttempt(IntPtr hwnd)
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;

            if (!_attempts.TryGetValue(hwnd, out var attempt) || now - attempt.FirstSeen > AttemptWindow)
            {
                _attempts[hwnd] = new Attempt(1, now);
                return true;
            }

            var count = attempt.Count + 1;
            _attempts[hwnd] = new Attempt(count, attempt.FirstSeen);

            if (count > MaxAttemptsPerWindow)
            {
                if (_uncooperative.Add(hwnd))
                {
                    _log($"התוכנית 0x{hwnd.ToInt64():X} ({GetClassNameSafe(hwnd)}) מחזירה את עצמה למסך-מלא — מפסיקים להתערב.");
                }

                return false;
            }

            return true;
        }
    }

    private static string GetClassNameSafe(IntPtr hwnd)
    {
        try
        {
            return GetClassName(hwnd);
        }
        catch (Exception)
        {
            return "לא ידוע";
        }
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        var length = Native.GetClassName(hwnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString() : string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        lock (_sync)
        {
            _attempts.Clear();
            _uncooperative.Clear();
        }
    }

    private readonly record struct Attempt(int Count, DateTime FirstSeen);
}
