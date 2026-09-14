using System;
using System.Collections.Generic;
using System.Linq;
using FrameFit.Core;
using FrameFit.Core.Abstractions;
using FrameFit.Core.Geometry;
using FrameFit.Core.Profiles;
using FrameFit.Platform.Windows;
using FrameFit.Platform.Windows.Diagnostics;
using FrameFit.Platform.Windows.Displays;
using FrameFit.Platform.Windows.Input;
using FrameFit.Platform.Windows.Logging;
using FrameFit.Platform.Windows.Overlay;
using FrameFit.Platform.Windows.Watching;
using FrameFit.Platform.Windows.WorkArea;

namespace FrameFit.App.Services;

/// <summary>מצב מנגנוני האכיפה ברגע נתון.</summary>
public sealed record SessionState(
    bool OverlayActive,
    bool WorkAreaActive,
    bool CursorClamped,
    bool WatcherActive,
    string TaskbarState,
    int RefitCount,
    int UncooperativeCount);

/// <summary>
/// המוח של התוכנה: מחבר בין הפרופילים, המסכים וארבעת מנגנוני האכיפה,
/// ודואג שכל שינוי יהיה הפיך (החלטה D4).
/// </summary>
public sealed class FrameFitSession : IDisposable
{
    private readonly WindowsDisplayProvider _displayProvider = new();
    private readonly ProfileStore _store;
    private readonly AppLog _log;
    private readonly OverlayHost _overlay = new();

    private WorkAreaHost? _workArea;
    private FullscreenWatcher? _watcher;
    private GlobalHotKey? _panicHotKey;
    private bool _disposed;

    public FrameFitSession(AppLog log)
    {
        _log = log;
        _store = new ProfileStore(AppPaths.SettingsFile);
        Settings = _store.Load(out var wasReset);

        // אם התהליך הקודם נהרג בעוד סרגל המשימות מוסתר — מחזירים אותו לפני כל פעולה אחרת.
        if (TaskbarHost.TryRecoverAbandoned(out var recoveryMessage))
        {
            _log.Write(recoveryMessage);
        }

        if (wasReset)
        {
            _log.Write("הגדרות לא נמצאו או היו פגומות — מתחילים במצב כבוי ובטוח.");
        }

        _panicHotKey = new GlobalHotKey(
            GlobalHotKey.PanicModifiers,
            GlobalHotKey.PanicVirtualKey,
            () =>
            {
                _log.Write("מקש המילוט הופעל — משחזר את מצב Windows.");
                RevertAll();
                PanicRequested?.Invoke();
            });

        if (!_panicHotKey.IsRegistered)
        {
            _log.Write($"אזהרה: רישום מקש המילוט נכשל (קוד {_panicHotKey.LastError}).");
        }
    }

    public event Action? PanicRequested;

    public SettingsDocument Settings { get; }

    public DisplayInfo? TargetDisplay { get; private set; }

    public MarginSet CurrentMargins { get; private set; } = MarginSet.Empty;

    public bool IsPreviewing { get; private set; }

    public bool IsApplied { get; private set; }

    public bool PanicKeyRegistered => _panicHotKey?.IsRegistered ?? false;

    public IReadOnlyList<DisplayInfo> RefreshDisplays()
    {
        var displays = _displayProvider.GetDisplays();
        _log.Write($"זוהו {displays.Count} מסכים.");
        return displays;
    }

    /// <summary>מציג תצוגה מקדימה בלבד — שכבת כיסוי, בלי לגעת בעכבר ובאזור העבודה.</summary>
    public void UpdatePreview(DisplayInfo display, MarginSet margins)
    {
        TargetDisplay = display;
        CurrentMargins = margins;
        IsPreviewing = true;

        var validate = VisibleAreaCalculator.Validate(display.Bounds, margins);
        if (!validate.IsValid)
        {
            _log.Write("התצוגה המקדימה לא הוצגה: הגדרת השוליים אינה תקינה.");
            return;
        }

        // סרגלי הכיול מוצגים רק בתצוגה מקדימה — שם הטכנאי צריך ליישר מול המסגרת.
        _overlay.Apply(display.Bounds, margins, OverlayStyle.Preview, visible: true);
    }

    public void StopPreview()
    {
        IsPreviewing = false;
        _overlay.Hide();
        _log.Write("התצוגה המקדימה כובתה.");
    }

    /// <summary>החלה מלאה: כל מנגנוני האכיפה, ושמירה כפרופיל.</summary>
    public bool Apply(DisplayInfo display, MarginSet margins, ProfileOptions options, out string message)
    {
        var validation = VisibleAreaCalculator.Validate(display.Bounds, margins);
        if (!validation.IsValid)
        {
            message = validation.Issue == MarginIssue.Negative
                ? "שוליים שליליים אינם חוקיים."
                : "האזור הגלוי קטן מדי. יש להקטין את השוליים.";
            return false;
        }

        // תמיד מתחילים ממצב נקי, כדי שלא יישארו שאריות מהחלה קודמת.
        StopInternal();

        TargetDisplay = display;
        CurrentMargins = margins;

        _overlay.Apply(display.Bounds, margins, OverlayStyle.Solid, visible: options.BlackoutMargins);

        if (options.ClampCursor)
        {
            var visible = VisibleAreaCalculator.Compute(display.Bounds, margins);
            CursorClamp.Apply(visible);
        }

        if (options.ReserveWorkArea)
        {
            _workArea = new WorkAreaHost(_log.Write);
            if (_workArea.TryApply(display.Bounds, margins, options.TaskbarMode, out var workAreaMessage))
            {
                _log.Write(workAreaMessage);
            }
            else
            {
                _log.Write($"אזהרה: {workAreaMessage}");
                _workArea.Dispose();
                _workArea = null;
            }
        }

        if (options.RefitFullscreen)
        {
            _watcher = new FullscreenWatcher(
                display.Bounds,
                margins,
                handleMaximizedWindows: !(_workArea?.IsActive ?? false),
                _log.Write);
            _watcher.Start();
        }

        IsApplied = true;
        IsPreviewing = false;

        SaveProfile(display, margins, options);

        message = "ההגדרה הוחלה.";
        _log.Write($"הוחל על {display.Describe()} — שוליים {margins}");
        return true;
    }

    public void SaveProfile(DisplayInfo display, MarginSet margins, ProfileOptions options)
    {
        var fingerprint = display.ToFingerprint();
        var profile = new FrameFitProfile
        {
            Id = ProfileMatcher.BuildId(fingerprint),
            Name = display.FriendlyName,
            Display = fingerprint,
            Margins = margins,
            Options = new ProfileOptions
            {
                BlackoutMargins = options.BlackoutMargins,
                ClampCursor = options.ClampCursor,
                RefitFullscreen = options.RefitFullscreen,
                ReserveWorkArea = options.ReserveWorkArea,
                TaskbarMode = options.TaskbarMode
            }
        };

        ProfileMatcher.Upsert(Settings, profile);
        Settings.ActiveProfileId = profile.Id;

        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex)
        {
            _log.Write($"שמירת ההגדרות נכשלה: {ex.Message}");
        }
    }

    /// <summary>
    /// מחזיר את המערכת למצב Windows נקי. זהו מסלול החירום, ולכן הוא לא זורק חריגות
    /// ולא תלוי במצב הפנימי.
    /// </summary>
    public void RevertAll()
    {
        try
        {
            StopInternal();

            IsApplied = false;
            IsPreviewing = false;

            Settings.ActiveProfileId = null;
            try
            {
                _store.Save(Settings);
            }
            catch (Exception)
            {
                // גם אם השמירה נכשלת — השחזור עצמו כבר בוצע.
            }

            _log.Write("כל ההגדרות שוחזרו למצב Windows נקי.");
        }
        catch (Exception ex)
        {
            _log.Write($"שגיאה בשחזור: {ex.Message}");
        }
    }

    /// <summary>מחזיר פרופיל שנשמר עבור התצוגה הזו, אם יש.</summary>
    public FrameFitProfile? FindProfile(DisplayInfo display) =>
        ProfileMatcher.FindBest(Settings, display.ToFingerprint());

    public SessionState GetState() => new(
        OverlayActive: _overlay.IsVisible && IsApplied,
        WorkAreaActive: _workArea?.IsActive ?? false,
        CursorClamped: CursorClamp.IsActive,
        WatcherActive: _watcher is not null,
        TaskbarState: DescribeTaskbarState(),
        RefitCount: _watcher?.RefitCount ?? 0,
        UncooperativeCount: _watcher?.UncooperativeWindows.Count ?? 0);

    /// <summary>מה נעשה עם סרגל המשימות של המסך המנוהל, בלשון האדם.</summary>
    private string DescribeTaskbarState()
    {
        if (_workArea is null)
        {
            return "מנגנון אזור העבודה כבוי";
        }

        if (_workArea.TaskbarMoved)
        {
            return "עוגן בתוך האזור הגלוי";
        }

        if (_workArea.TaskbarHidden)
        {
            return "הוסתר";
        }

        return _workArea.TaskbarMode switch
        {
            TaskbarMode.LeaveInPlace => "ללא התערבות (נשאר במקומו)",
            _ => _workArea.Taskbar.IsAttached
                ? "נשאר במקומו — Windows החזיר אותו (אי אפשר להזיז את הסרגל)"
                : "לא טופל — אין סרגל משימות במסך הזה"
        };
    }

    public string BuildDiagnostics() => DiagnosticsService.BuildReport(
        _displayProvider.GetDisplays(),
        TargetDisplay,
        CurrentMargins,
        overlayActive: _overlay.IsVisible,
        workAreaActive: _workArea?.IsActive ?? false,
        cursorClamped: CursorClamp.IsActive,
        watcherActive: _watcher is not null,
        refitCount: _watcher?.RefitCount ?? 0,
        uncooperativeCount: _watcher?.UncooperativeWindows.Count ?? 0,
        workAreaStatus: BuildWorkAreaStatus());

    /// <summary>מצב אזור העבודה וסרגל המשימות לדוח האבחון.</summary>
    private WorkAreaStatus? BuildWorkAreaStatus()
    {
        if (TargetDisplay is null)
        {
            return null;
        }

        return new WorkAreaStatus(
            TaskbarState: DescribeTaskbarState(),
            ExpectedWorkArea: _workArea?.AppliedPlan?.ExpectedWorkArea,
            Shortfall: _workArea?.Shortfall ?? ReservedInsets.None,
            TaskbarAnchor: _workArea?.TaskbarAnchor ?? 0,
            TaskbarDescription: _workArea?.Taskbar is { } taskbar
                ? taskbar.Describe(TargetDisplay.Bounds, CurrentMargins)
                : "מנגנון אזור העבודה כבוי");
    }

    private void StopInternal()
    {
        _watcher?.Dispose();
        _watcher = null;

        _workArea?.Dispose();
        _workArea = null;

        CursorClamp.Release();
        _overlay.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopInternal();
        _overlay.Dispose();
        _panicHotKey?.Dispose();
        _panicHotKey = null;
    }
}
