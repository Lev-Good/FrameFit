using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using FrameFit.App.Infrastructure;
using FrameFit.App.Services;
using FrameFit.Core.Abstractions;
using FrameFit.Core.Geometry;
using FrameFit.Core.Profiles;
using FrameFit.Platform.Windows;
using FrameFit.Platform.Windows.Diagnostics;
using FrameFit.Platform.Windows.Logging;

namespace FrameFit.App.ViewModels;

/// <summary>
/// ממשק הניהול: בחירת מסך, ארבעה שוליים בזמן אמת, תצוגה מקדימה ואכיפה.
/// </summary>
public sealed class MainViewModel : BindableBase, IDisposable
{
    /// <summary>גודל המפה המקדימה בחלון הניהול (בפיקסלים לוגיים).</summary>
    public const double MiniMapWidth = 320;

    public const double MiniMapHeight = 180;

    /// <summary>אחרי כמה זמן תצוגה מקדימה מתבטלת מעצמה אם לא אושרה (בטיחות, D4).</summary>
    private static readonly TimeSpan PreviewTimeout = TimeSpan.FromSeconds(20);

    private const int MaxLogLines = 300;

    private readonly FrameFitSession _session;
    private readonly DispatcherTimer _previewTimer;
    private readonly AppLog _log;

    private DisplayInfo? _selectedDisplay;
    private MarginSet _margins = MarginSet.Empty;
    private bool _isPreviewing;
    private string _statusText = "מוכן.";
    private bool _suppressReload;

    public MainViewModel(FrameFitSession session, AppLog log)
    {
        _session = session;
        _log = log;

        _previewTimer = new DispatcherTimer { Interval = PreviewTimeout };
        _previewTimer.Tick += (_, _) => ExpirePreview();

        _log.LineWritten += OnLogLine;

        Displays = new ObservableCollection<DisplayInfo>(_session.RefreshDisplays());
        _selectedDisplay = Displays.FirstOrDefault(d => !d.IsPrimary) ?? Displays.FirstOrDefault();

        PreviewCommand = new RelayCommand(TogglePreview);
        ApplyCommand = new RelayCommand(Apply);
        RevertCommand = new RelayCommand(RevertAll);
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics);
        ResetMarginsCommand = new RelayCommand(() => SetMargins(MarginSet.Empty));
        NudgeCommand = new RelayCommand<string>(Nudge);

        LoadProfileForSelection();
        UpdateDerived();
    }

    public ObservableCollection<DisplayInfo> Displays { get; }

    public ObservableCollection<string> LogLines { get; } = new();

    public RelayCommand PreviewCommand { get; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand RevertCommand { get; }

    public RelayCommand CopyDiagnosticsCommand { get; }

    public RelayCommand ResetMarginsCommand { get; }

    public RelayCommand<string> NudgeCommand { get; }

    public DisplayInfo? SelectedDisplay
    {
        get => _selectedDisplay;
        set
        {
            if (Set(ref _selectedDisplay, value))
            {
                LoadProfileForSelection();
                UpdateDerived();
            }
        }
    }

    public int Left
    {
        get => _margins.Left;
        set => SetMargin(MarginSide.Left, value);
    }

    public int Right
    {
        get => _margins.Right;
        set => SetMargin(MarginSide.Right, value);
    }

    public int Top
    {
        get => _margins.Top;
        set => SetMargin(MarginSide.Top, value);
    }

    public int Bottom
    {
        get => _margins.Bottom;
        set => SetMargin(MarginSide.Bottom, value);
    }

    public int MaxLeft => MaxFor(MarginSide.Left);

    public int MaxRight => MaxFor(MarginSide.Right);

    public int MaxTop => MaxFor(MarginSide.Top);

    public int MaxBottom => MaxFor(MarginSide.Bottom);

    /// <summary>השוליים מוקטנים לתצוגה המקדימה בחלון, כדי לראות את הפרופורציה.</summary>
    public Thickness MiniMargins
    {
        get
        {
            if (SelectedDisplay is null)
            {
                return new Thickness(0);
            }

            var scaleX = MiniMapWidth / SelectedDisplay.Bounds.Width;
            var scaleY = MiniMapHeight / SelectedDisplay.Bounds.Height;

            return new Thickness(
                Math.Round(_margins.Left * scaleX, 1),
                Math.Round(_margins.Top * scaleY, 1),
                Math.Round(_margins.Right * scaleX, 1),
                Math.Round(_margins.Bottom * scaleY, 1));
        }
    }

    public string VisibleAreaText
    {
        get
        {
            if (SelectedDisplay is null)
            {
                return "—";
            }

            var visible = VisibleAreaCalculator.Compute(SelectedDisplay.Bounds, _margins);
            return $"{Math.Max(0, visible.Width)} × {Math.Max(0, visible.Height)}";
        }
    }

    public string MonitorSizeText => SelectedDisplay is null
        ? "—"
        : $"{SelectedDisplay.Bounds.Width} × {SelectedDisplay.Bounds.Height}";

    public string VisibleRatioText
    {
        get
        {
            if (SelectedDisplay is null)
            {
                return string.Empty;
            }

            var ratio = VisibleAreaCalculator.VisibleRatio(SelectedDisplay.Bounds, _margins);
            return $"{ratio:P1} מהמסך נשאר גלוי";
        }
    }

    public string AsymmetryText
    {
        get
        {
            if (_margins.IsZero)
            {
                return string.Empty;
            }

            return _margins.IsAsymmetric
                ? "השוליים אינם סימטריים — זו הסיבה שנדרשת שכבת ניהול החלונות."
                : "השוליים סימטריים.";
        }
    }

    public string PhysicalHint
    {
        get
        {
            if (SelectedDisplay?.PhysicalWidthMm is null || SelectedDisplay.PhysicalHeightMm is null)
            {
                return string.Empty;
            }

            var width = VisibleAreaCalculator.PixelsToMillimeters(
                _margins.Left + _margins.Right,
                SelectedDisplay.Bounds.Width,
                SelectedDisplay.PhysicalWidthMm);

            var height = VisibleAreaCalculator.PixelsToMillimeters(
                _margins.Top + _margins.Bottom,
                SelectedDisplay.Bounds.Height,
                SelectedDisplay.PhysicalHeightMm);

            if (width is null || height is null)
            {
                return string.Empty;
            }

            return $"רוחב פאנל: {SelectedDisplay.PhysicalWidthMm} מ״מ · " +
                   $"שוליים שהוסתרו: {width.Value:F0} מ״מ אופקית, {height.Value:F0} מ״מ אנכית";
        }
    }

    public string MarginHint
    {
        get
        {
            if (SelectedDisplay is null)
            {
                return string.Empty;
            }

            var validation = VisibleAreaCalculator.Validate(SelectedDisplay.Bounds, _margins);
            if (validation.IsValid)
            {
                return "האזור הגלוי תקין.";
            }

            return validation.Issue == MarginIssue.TooSmall
                ? "האזור הגלוי קטן מהמינימום — יש להקטין את השוליים."
                : "ערכי שוליים אינם חוקיים.";
        }
    }

    public bool BlackoutMargins
    {
        get => Options.BlackoutMargins;
        set
        {
            if (Options.BlackoutMargins != value)
            {
                Options.BlackoutMargins = value;
                Raise();
            }
        }
    }

    public bool ClampCursor
    {
        get => Options.ClampCursor;
        set
        {
            if (Options.ClampCursor != value)
            {
                Options.ClampCursor = value;
                Raise();
            }
        }
    }

    public bool RefitFullscreen
    {
        get => Options.RefitFullscreen;
        set
        {
            if (Options.RefitFullscreen != value)
            {
                Options.RefitFullscreen = value;
                Raise();
            }
        }
    }

    public bool ReserveWorkArea
    {
        get => Options.ReserveWorkArea;
        set
        {
            if (Options.ReserveWorkArea != value)
            {
                Options.ReserveWorkArea = value;
                Raise();
            }
        }
    }

    /// <summary>סרגל המשימות יוסתר כל עוד ההגדרה פעילה.</summary>
    public bool TaskbarHide
    {
        get => Options.TaskbarMode == TaskbarMode.HideInHiddenArea;
        set
        {
            if (value)
            {
                SetTaskbarMode(TaskbarMode.HideInHiddenArea);
            }
        }
    }

    /// <summary>לא נוגעים בסרגל המשימות; הוא נשאר במקומו ותופס את מקומו באזור העבודה.</summary>
    public bool TaskbarLeave
    {
        get => Options.TaskbarMode == TaskbarMode.LeaveInPlace;
        set
        {
            if (value)
            {
                SetTaskbarMode(TaskbarMode.LeaveInPlace);
            }
        }
    }

    private void SetTaskbarMode(TaskbarMode mode)
    {
        if (Options.TaskbarMode == mode)
        {
            return;
        }

        Options.TaskbarMode = mode;
        Raise(nameof(TaskbarHide));
        Raise(nameof(TaskbarLeave));
    }

    /// <summary>
    /// ממיר מצב סרגל שאינו נתמך עוד למצב שנתמך. מצב "ניסיון העברה" נמדד כבלתי אפשרי
    /// ב-Windows 11 (D14), ולכן פרופיל שמור שמכיל אותו נטען כ"יישאר במקומו".
    /// </summary>
    private static TaskbarMode NormalizeTaskbarMode(TaskbarMode mode) =>
        mode == TaskbarMode.MoveIntoVisibleArea ? TaskbarMode.LeaveInPlace : mode;

    public bool StartWithWindows
    {
        get => _session.Settings.StartWithWindows;
        set
        {
            if (_session.Settings.StartWithWindows == value)
            {
                return;
            }

            _session.Settings.StartWithWindows = value;
            Raise();

            if (AutoStart.TrySet(value, out var error))
            {
                StatusText = value
                    ? "התוכנה תופעל אוטומטית עם עליית Windows."
                    : "ההפעלה האוטומטית כובתה.";
            }
            else
            {
                StatusText = error;
            }
        }
    }

    public bool IsPreviewing
    {
        get => _isPreviewing;
        private set
        {
            if (Set(ref _isPreviewing, value))
            {
                Raise(nameof(PreviewButtonText));
            }
        }
    }

    public string PreviewButtonText => IsPreviewing ? "כבה תצוגה מקדימה" : "הצג תצוגה מקדימה";

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public bool PanicKeyRegistered => _session.PanicKeyRegistered;

    public string PanicHint =>
        "מקש החירום: Ctrl + Alt + Shift + F12 — משחזר את מצב Windows בכל רגע, גם כשהעכבר חסום.";

    private ProfileOptions Options { get; } = new();

    private void OnLogLine(string line)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            AppendLine(line);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => AppendLine(line)));
        }
    }

    private void AppendLine(string line)
    {
        LogLines.Add(line);
        while (LogLines.Count > MaxLogLines)
        {
            LogLines.RemoveAt(0);
        }
    }

    private void LoadProfileForSelection()
    {
        if (SelectedDisplay is null || _suppressReload)
        {
            return;
        }

        _suppressReload = true;

        var profile = _session.FindProfile(SelectedDisplay);
        if (profile is not null)
        {
            Options.BlackoutMargins = profile.Options.BlackoutMargins;
            Options.ClampCursor = profile.Options.ClampCursor;
            Options.RefitFullscreen = profile.Options.RefitFullscreen;
            Options.ReserveWorkArea = profile.Options.ReserveWorkArea;
            Options.TaskbarMode = NormalizeTaskbarMode(profile.Options.TaskbarMode);

            _margins = profile.Margins;

            Raise(nameof(BlackoutMargins));
            Raise(nameof(ClampCursor));
            Raise(nameof(RefitFullscreen));
            Raise(nameof(ReserveWorkArea));
            Raise(nameof(TaskbarHide));
            Raise(nameof(TaskbarLeave));
            StatusText = $"נטענה הגדרה שמורה עבור {SelectedDisplay.FriendlyName}.";
        }
        else
        {
            _margins = MarginSet.Empty;
            StatusText = "לא נמצאה הגדרה שמורה למסך הזה — מתחילים מאפס.";
        }

        _suppressReload = false;
    }

    private int MaxFor(MarginSide side)
    {
        if (SelectedDisplay is null)
        {
            return 0;
        }

        return VisibleAreaCalculator.MaximumForSide(SelectedDisplay.Bounds, _margins, side);
    }

    private void SetMargin(MarginSide side, int value)
    {
        if (SelectedDisplay is null)
        {
            return;
        }

        var clamped = Math.Max(0, Math.Min(value, MaxFor(side)));
        if (_margins[side] == clamped)
        {
            return;
        }

        _margins = _margins.With(side, clamped);
        RaiseForSide(side);
        UpdateDerived();

        if (IsPreviewing)
        {
            RefreshPreview();
        }
    }

    private void SetMargins(MarginSet margins)
    {
        if (SelectedDisplay is null)
        {
            return;
        }

        _margins = VisibleAreaCalculator.Clamp(SelectedDisplay.Bounds, margins);
        Raise(nameof(Left));
        Raise(nameof(Right));
        Raise(nameof(Top));
        Raise(nameof(Bottom));
        UpdateDerived();

        if (IsPreviewing)
        {
            RefreshPreview();
        }
    }

    private void RaiseForSide(MarginSide side)
    {
        switch (side)
        {
            case MarginSide.Left:
                Raise(nameof(Left));
                break;
            case MarginSide.Right:
                Raise(nameof(Right));
                break;
            case MarginSide.Top:
                Raise(nameof(Top));
                break;
            case MarginSide.Bottom:
                Raise(nameof(Bottom));
                break;
        }
    }

    private void Nudge(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        // התחביר: "side:delta", לדוגמה left:10
        var parts = payload.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var delta))
        {
            return;
        }

        var side = parts[0].ToLowerInvariant() switch
        {
            "left" => MarginSide.Left,
            "right" => MarginSide.Right,
            "top" => MarginSide.Top,
            "bottom" => MarginSide.Bottom,
            _ => (MarginSide?)null
        };

        if (side is null)
        {
            return;
        }

        SetMargin(side.Value, _margins[side.Value] + delta);
    }

    private void UpdateDerived()
    {
        Raise(nameof(MaxLeft));
        Raise(nameof(MaxRight));
        Raise(nameof(MaxTop));
        Raise(nameof(MaxBottom));
        Raise(nameof(MiniMargins));
        Raise(nameof(VisibleAreaText));
        Raise(nameof(VisibleRatioText));
        Raise(nameof(MonitorSizeText));
        Raise(nameof(AsymmetryText));
        Raise(nameof(PhysicalHint));
        Raise(nameof(MarginHint));
    }

    private void TogglePreview()
    {
        if (IsPreviewing)
        {
            StopPreview("התצוגה המקדימה כובתה.");
            return;
        }

        if (SelectedDisplay is null)
        {
            StatusText = "לא זוהה מסך.";
            return;
        }

        if (!VisibleAreaCalculator.Validate(SelectedDisplay.Bounds, _margins).IsValid)
        {
            StatusText = MarginHint;
            return;
        }

        RefreshPreview();
        IsPreviewing = true;
        _previewTimer.Stop();
        _previewTimer.Start();

        StatusText = $"תצוגה מקדימה על {SelectedDisplay.FriendlyName}. " +
                     "כוונן את השוליים עד שהמלבן מתיישב בדיוק בתוך המסגרת.";
    }

    private void RefreshPreview()
    {
        if (SelectedDisplay is null)
        {
            return;
        }

        _session.UpdatePreview(SelectedDisplay, _margins);

        // כל שינוי מאריך את הזמן שנותר לפני ביטול אוטומטי.
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void ExpirePreview()
    {
        _previewTimer.Stop();
        _session.StopPreview();
        IsPreviewing = false;
        StatusText = "התצוגה המקדימה בוטלה אוטומטית (בטיחות). אפשר להחיל כדי לשמור.";
    }

    private void StopPreview(string message)
    {
        _previewTimer.Stop();
        _session.StopPreview();
        IsPreviewing = false;
        StatusText = message;
    }

    private void Apply()
    {
        if (SelectedDisplay is null)
        {
            StatusText = "לא זוהה מסך.";
            return;
        }

        _previewTimer.Stop();

        if (_session.Apply(SelectedDisplay, _margins, Options, out var message))
        {
            IsPreviewing = false;
            StatusText = message + " ההגדרה נשמרה ותוחל גם בהפעלה הבאה.";
            Raise(nameof(StartWithWindows));
        }
        else
        {
            StatusText = message;
        }
    }

    private void RevertAll()
    {
        _previewTimer.Stop();
        _session.RevertAll();
        IsPreviewing = false;
        StatusText = "כל השינויים הוסרו. Windows חזר למצב רגיל.";
    }

    private void CopyDiagnostics()
    {
        try
        {
            var report = _session.BuildDiagnostics();
            Clipboard.SetText(report);
            StatusText = "דוח האבחון הועתק ללוח.";
        }
        catch (Exception ex)
        {
            StatusText = $"העתקת דוח האבחון נכשלה: {ex.Message}";
        }
    }

    /// <summary>מחזיר את הגדרת ברירת המחדל של המסך, בלי לשנות את המצב בפועל.</summary>
    public void ResetMargins() => ResetMarginsCommand.Execute(null);

    /// <summary>מפעיל את ההגדרה השמורה בעליית התוכנה, אם קיימת.</summary>
    public bool TryAutoApply()
    {
        if (_session.Settings.ActiveProfileId is null)
        {
            return false;
        }

        var display = SelectedDisplay;
        if (display is null)
        {
            return false;
        }

        var profile = _session.FindProfile(display);
        if (profile is null || profile.Margins.IsZero)
        {
            return false;
        }

        return _session.Apply(display, profile.Margins, profile.Options, out _);
    }

    public void Dispose()
    {
        _log.LineWritten -= OnLogLine;
        _previewTimer.Stop();
        _previewTimer.Tick -= (_, _) => ExpirePreview();
    }
}
