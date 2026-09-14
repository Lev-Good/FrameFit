using System.Runtime.InteropServices;
using FrameFit.Core.Geometry;
using FrameFit.Core.Profiles;
using FrameFit.Platform.Windows.Interop;

namespace FrameFit.Platform.Windows.WorkArea;

/// <summary>
/// שכבה 1 — צמצום "אזור העבודה" של Windows דרך סרגלי יישום (AppBar).
///
/// הרגישות כאן היא ש-Windows כבר הקצה מקום משלו לפני שהגענו: סרגל המשימות תופס את
/// שורת התחתית של המסך, וסרגל יישום שמוזמן על אותו צד נדחק מעליו. הזמנת שוליים בגובה
/// מלא הייתה איפוא מקטינה את אזור העבודה ביותר מהנדרש — ומשאירה רצועה בגובה סרגל
/// המשימות בתוך האזור הגלוי. לכן אנו מודדים קודם מה כבר הוקצה, ומזמינים בכל צד את
/// ההפרש בלבד (ראו <see cref="WorkAreaCalculator"/>).
/// </summary>
public sealed class WorkAreaHost : IDisposable
{
    private const string CallbackMessageName = "FrameFitAppBarMessage";

    private readonly List<AppBar> _bars = new();
    private readonly TaskbarHost _taskbar;
    private readonly Action<string> _log;

    private bool _disposed;
    private PixelRect _monitor;
    private MarginSet _margins = MarginSet.Empty;

    public WorkAreaHost(Action<string>? log = null)
    {
        _log = log ?? (_ => { });
        _taskbar = new TaskbarHost(_log);
    }

    public bool IsActive => _bars.Count > 0;

    /// <summary>האם FrameFit הסתיר כרגע את סרגל המשימות של המסך המנוהל.</summary>
    public bool TaskbarHidden => _taskbar.IsHiddenByUs;

    /// <summary>האם FrameFit העביר כרגע את סרגל המשימות אל תוך האזור הגלוי.</summary>
    public bool TaskbarMoved => _taskbar.IsMovedByUs;

    /// <summary>הטיפול בסרגל המשימות שבו השתמשנו בהחלה האחרונה.</summary>
    public TaskbarMode TaskbarMode { get; private set; } = TaskbarMode.LeaveInPlace;

    /// <summary>סרגל המשימות שזוהה במסך המנוהל (אם יש).</summary>
    public TaskbarHost Taskbar => _taskbar;

    /// <summary>התוכנית שהוחלה לאחרונה, או null אם לא הוחל דבר.</summary>
    public WorkAreaPlan? AppliedPlan { get; private set; }

    /// <summary>הפיקסלים של האזור הגלוי שההקצאות הקיימות תופסות בכל זאת.</summary>
    public ReservedInsets Shortfall => AppliedPlan?.Shortfall ?? ReservedInsets.None;

    /// <summary>פיקסלים בתחתית האזור הגלוי שהוקצו לסרגל המשימות המעוגן.</summary>
    public int TaskbarAnchor => AppliedPlan?.BottomAnchor ?? 0;

    /// <summary>
    /// מנסה להזמין את ארבעת השוליים כמקום שמור. מחזיר false עם הסבר אם משהו נכשל.
    /// </summary>
    /// <param name="taskbarMode">
    /// מה לעשות עם סרגל המשימות של המסך הזה. הפעולה מתבצעת לפני המדידה, כי ייתכן
    /// שהיא משנה את אזור העבודה עצמו.
    /// </param>
    public bool TryApply(PixelRect monitor, MarginSet margins, TaskbarMode taskbarMode, out string message)
    {
        Revert();

        _monitor = monitor;
        _margins = margins;
        TaskbarMode = taskbarMode;

        var beforeTaskbar = MeasuredReservation(monitor);

        if (taskbarMode != TaskbarMode.LeaveInPlace)
        {
            if (!_taskbar.TryAttach(monitor, out var attachMessage))
            {
                _log(attachMessage);
            }
            else
            {
                var visible = VisibleAreaCalculator.Compute(monitor, margins);
                var applied = taskbarMode == TaskbarMode.HideInHiddenArea
                    ? _taskbar.Hide(out var note)
                    : _taskbar.MoveInto(visible, out note);

                _log(applied ? note : $"אזהרה: {note}");
            }
        }

        // מה שכבר הוקצה במסך הזה (סרגל המשימות, סרגלי יישום אחרים) — נמדד בזמן אמת,
        // אחרי הפעולה על הסרגל, כי ייתכן שהיא שינתה את אזור העבודה.
        var existing = _taskbar.IsHandledByUs
            ? MeasuredReservation(monitor, beforeTaskbar)
            : MeasuredReservation(monitor);

        // סרגל משימות שנעגן בתוך האזור הגלוי תופס בו מקום אמיתי. המקום הזה נגרע מאזור
        // העבודה (ולא נחשב לגזילה מהשוליים), כדי שחלון ממוקם יסתיים מעל הסרגל במקום
        // להיעלם מתחתיו.
        var anchor = _taskbar.IsMovedByUs ? _taskbar.Rect.Height : 0;

        var plan = WorkAreaCalculator.Plan(monitor, margins, existing, anchor);
        AppliedPlan = plan;

        _log($"הקצאות קיימות באזור העבודה: {existing}; " +
             $"הוזמנו {plan.Strips.Count} סרגלים; אזור עבודה מצופה: {plan.ExpectedWorkArea}" +
             (anchor > 0 ? $"; {anchor} פיקסלים בתחתית הוקצו לסרגל המשימות המעוגן." : "."));

        var callbackMessage = MessageWindow.RegisterMessage(CallbackMessageName);

        try
        {
            foreach (var (side, rect) in plan.Strips)
            {
                var bar = new AppBar(ToEdge(side), callbackMessage);
                if (!bar.TryRegister(rect, out var error))
                {
                    message = $"הזמנת השוליים נכשלה (צד {side}): {error}";
                    Revert();
                    return false;
                }

                _bars.Add(bar);
            }
        }
        catch (Exception ex)
        {
            message = $"הזמנת השוליים נכשלה: {ex.Message}";
            Revert();
            return false;
        }

        message = BuildSummary(plan, monitor);
        return true;
    }

    /// <summary>מסיר את כל הסרגלים, מחזיר את סרגל המשימות ומשחזר את אזור העבודה.</summary>
    public void Revert()
    {
        foreach (var bar in _bars)
        {
            bar.Dispose();
        }

        _bars.Clear();

        AppliedPlan = null;

        // מחזירים את סרגל המשימות *אחרי* הסרת הסרגלים, כדי שאזור העבודה יחזור למצבו המקורי.
        _taskbar.Restore();
    }

    /// <summary>מה שמערכת ההפעלה כבר הקצתה באזור העבודה של המסך הזה.</summary>
    private static ReservedInsets MeasuredReservation(PixelRect monitor) =>
        WorkAreaProbe.TryGetWorkArea(monitor) is { } workArea
            ? ReservedInsets.Between(monitor, workArea)
            : ReservedInsets.None;

    /// <summary>
    /// ה-Shell מעדכן את אזור העבודה בהשהיה קצרה אחרי שינוי בסרגל המשימות. אם המדידה
    /// עוד לא השתנתה, ממתינים לה בייצוב של עד 300 מ״ש — אחרת ההזמנה שלנו הייתה מקזזת
    /// הקצאה שכבר אינה קיימת, וחלון ממוקם היה חורג אל תוך האזור המוסתר.
    /// </summary>
    private static ReservedInsets MeasuredReservation(PixelRect monitor, ReservedInsets beforeHiding)
    {
        var deadline = Environment.TickCount64 + 300;

        while (true)
        {
            var measured = MeasuredReservation(monitor);

            if (measured != beforeHiding || Environment.TickCount64 >= deadline)
            {
                return measured;
            }

            Thread.Sleep(40);
        }
    }

    private string BuildSummary(WorkAreaPlan plan, PixelRect monitor)
    {
        if (plan.IsExact)
        {
            return $"אזור העבודה צומצם כך שיכסה בדיוק את האזור הגלוי ({plan.Visible}).";
        }

        // עיגון הסרגל בתוך האזור הגלוי הוא מצב מכוון: התוכן והסרגל ממלאים יחד את כולו,
        // וכאן התוכן מסתיים מעל הסרגל במקום להיערם מתחתיו.
        if (plan.BottomAnchor > 0 && _taskbar.MoveTarget is { } anchored)
        {
            return $"אזור העבודה צומצם ל-{plan.ExpectedWorkArea}; סרגל המשימות עוגן ב-{anchored} " +
                   $"ומקומו ({plan.BottomAnchor} פיקסלים) נגרע מהאזור הגלוי.";
        }

        return $"אזור העבודה צומצם ל-{plan.ExpectedWorkArea}, אך {DescribeShortfall(plan.Shortfall)}";
    }

    private static string DescribeShortfall(ReservedInsets shortfall) =>
        $"רצועה בתוך האזור הגלוי נשארת תפוסה (שמאל {shortfall.Left}, למעלה {shortfall.Top}, " +
        $"ימין {shortfall.Right}, למטה {shortfall.Bottom} פיקסלים) — סרגל המשימות ארוך מהשוליים שהוגדרו.";

    private static uint ToEdge(MarginSide side) => side switch
    {
        MarginSide.Left => Native.ABE_LEFT,
        MarginSide.Top => Native.ABE_TOP,
        MarginSide.Right => Native.ABE_RIGHT,
        MarginSide.Bottom => Native.ABE_BOTTOM,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Revert();
        _taskbar.Dispose();
    }

    /// <summary>סרגל יישום בודד — חלון הודעות אחד לכל צד.</summary>
    private sealed class AppBar : IDisposable
    {
        private readonly uint _edge;
        private readonly uint _callbackMessage;
        private readonly MessageWindow _window;
        private PixelRect _rect;
        private bool _registered;

        public AppBar(uint edge, uint callbackMessage)
        {
            _edge = edge;
            _callbackMessage = callbackMessage;
            _window = new MessageWindow { MessageHandler = OnMessage };
        }

        public bool TryRegister(PixelRect rect, out string error)
        {
            _rect = rect;

            var data = CreateData();
            if (Native.SHAppBarMessage(Native.ABM_NEW, ref data) == UIntPtr.Zero)
            {
                error = "המערכת סירבה לרשום סרגל יישום.";
                return false;
            }

            _registered = true;

            if (!SetPosition(out error))
            {
                return false;
            }

            NotifyWorkAreaChanged();
            error = string.Empty;
            return true;
        }

        private bool SetPosition(out string error)
        {
            var data = CreateData();

            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref data);

            // המערכת מציבה את הסרגל בצמוד למה שכבר תפוס באותו צד; אנחנו קובעים רק את הגובה
            // (או הרוחב) שביקשנו — ולכן ההזמנה משלימה את ההקצאה הקיימת ולא מצטברת מעליה.
            switch (_edge)
            {
                case Native.ABE_LEFT:
                    data.rc.Right = data.rc.Left + _rect.Width;
                    break;
                case Native.ABE_RIGHT:
                    data.rc.Left = data.rc.Right - _rect.Width;
                    break;
                case Native.ABE_TOP:
                    data.rc.Bottom = data.rc.Top + _rect.Height;
                    break;
                case Native.ABE_BOTTOM:
                    data.rc.Top = data.rc.Bottom - _rect.Height;
                    break;
            }

            if (Native.SHAppBarMessage(Native.ABM_SETPOS, ref data) == UIntPtr.Zero)
            {
                error = "המערכת סירבה לקבוע את מיקום הסרגל.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private Native.APPBARDATA CreateData() => new()
        {
            cbSize = Marshal.SizeOf<Native.APPBARDATA>(),
            hWnd = _window.Handle,
            uCallbackMessage = _callbackMessage,
            uEdge = _edge,
            rc = new Native.RECT(_rect.X, _rect.Y, _rect.Right, _rect.Bottom),
            lParam = IntPtr.Zero
        };

        /// <summary>שולח ABN_POSCHANGED לכל החלונות כדי שיידעו שהאזור השתנה.</summary>
        private static void NotifyWorkAreaChanged()
        {
            var data = new Native.APPBARDATA
            {
                cbSize = Marshal.SizeOf<Native.APPBARDATA>()
            };

            Native.SHAppBarMessage(Native.ABN_POSCHANGED, ref data);
        }

        private IntPtr? OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == _callbackMessage)
            {
                switch ((uint)lParam.ToInt64())
                {
                    case Native.ABN_POSCHANGED:
                        // מישהו אחר שינה את אזור העבודה — מייצבים את שלנו מחדש.
                        SetPosition(out _);
                        return IntPtr.Zero;
                    case Native.ABN_FULLSCREENAPP:
                    case Native.ABN_WINDOWARRANGE:
                        return IntPtr.Zero;
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (_registered)
            {
                var data = CreateData();
                Native.SHAppBarMessage(Native.ABM_REMOVE, ref data);
                _registered = false;
            }

            _window.Dispose();
        }
    }
}
