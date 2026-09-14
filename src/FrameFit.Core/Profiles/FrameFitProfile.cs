using FrameFit.Core.Geometry;

namespace FrameFit.Core.Profiles;

/// <summary>
/// מה נעשה עם סרגל המשימות של Windows במסך המנוהל.
///
/// סרגל המשימות מוצמד לשולי המסך, מקצה לעצמו מקום באזור העבודה ואינו נעלם מעצמו.
/// כאשר השוליים המוסתרים מכסים את מקומו הטבעי (שורת התחתית של המסך), הוא יושב בתוך
/// האזור המוסתר — לא שמיש — ובמקביל הוא ממשיך לגזול מקום מהאזור הגלוי.
///
/// <b>מגבלה שנמדדה ב-Windows 11:</b> אי אפשר להזיז את סרגל המשימות. ה-Shell מחזיק אותו
/// בקצה המסך ומחזיר אותו לשם מיד לאחר כל הזזה, גם כשקריאת ההזזה מדווחת על הצלחה.
/// לכן ברירת המחדל היא אי-התערבות — הסרגל נשאר במקומו, וצמצום אזור העבודה מקזז את
/// ההקצאה שלו כך שאין רצועה אבודה באזור הגלוי.
/// </summary>
public enum TaskbarMode
{
    /// <summary>
    /// ניסיון לדחוף את הסרגל אל תוך האזור הגלוי ולעגן אותו בשוליו התחתונים.
    /// **ב-Windows 11 זה נכשל תמיד** — הפעולה מזהה זאת, מחזירה את הסרגל למקומו ומדווחת
    /// בלוג, ולא גוזלת רצועה מהאזור הגלוי.
    /// </summary>
    MoveIntoVisibleArea,

    /// <summary>הסרגל מוסתר כל עוד ההגדרה פעילה, ומוחזר בכל מסלול יציאה.</summary>
    HideInHiddenArea,

    /// <summary>אין התערבות — הסרגל נשאר במקומו, ותופס את מקומו באזור העבודה (ברירת מחדל).</summary>
    LeaveInPlace
}

/// <summary>אילו מנגנוני אכיפה מופעלים עבור פרופיל.</summary>
public sealed class ProfileOptions
{
    /// <summary>שכבת כיסוי שחורה מעל השוליים.</summary>
    public bool BlackoutMargins { get; set; } = true;

    /// <summary>חסימת סמן העכבר לאזור הגלוי.</summary>
    public bool ClampCursor { get; set; } = true;

    /// <summary>התאמת חלונות מסך-מלא לאזור הגלוי (שכבה 2 — ליבת המוצר).</summary>
    public bool RefitFullscreen { get; set; } = true;

    /// <summary>
    /// צמצום אזור העבודה של Windows דרך AppBar (שכבה 1).
    /// ספיק S0.1 אומת בהצלחה ב-Windows 11 (build 26200): ארבעה סרגלים מצמצמים
    /// את אזור העבודה בפועל, ולכן מקסם ו-Snap מכבדים את השוליים. לכן ברירת המחדל פעילה.
    /// </summary>
    public bool ReserveWorkArea { get; set; } = true;

    /// <summary>
    /// הטיפול בסרגל המשימות של Windows במסך המנוהל. ראו <see cref="TaskbarMode"/>.
    /// ברירת המחדל — אי-התערבות: ב-Windows 11 אי אפשר להזיז את הסרגל (נמדד), והסתרתו
    /// היא בחירה של המשתמש.
    /// </summary>
    public TaskbarMode TaskbarMode { get; set; } = TaskbarMode.LeaveInPlace;
}

/// <summary>פרופיל הגדרה לתצוגה אחת.</summary>
public sealed class FrameFitProfile
{
    public string Id { get; set; } = "default";

    public string Name { get; set; } = string.Empty;

    public DisplayFingerprint Display { get; set; } = DisplayFingerprint.Unknown;

    public MarginSet Margins { get; set; } = MarginSet.Empty;

    public ProfileOptions Options { get; set; } = new();

    public FrameFitProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Display = Display with { },
        Margins = Margins,
        Options = new ProfileOptions
        {
            BlackoutMargins = Options.BlackoutMargins,
            ClampCursor = Options.ClampCursor,
            RefitFullscreen = Options.RefitFullscreen,
            ReserveWorkArea = Options.ReserveWorkArea,
            TaskbarMode = Options.TaskbarMode
        }
    };
}

/// <summary>מסמך ההגדרות שנשמר לדיסק.</summary>
public sealed class SettingsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool StartWithWindows { get; set; }

    public string Language { get; set; } = "he-IL";

    public string? ActiveProfileId { get; set; }

    public List<FrameFitProfile> Profiles { get; set; } = new();
}
