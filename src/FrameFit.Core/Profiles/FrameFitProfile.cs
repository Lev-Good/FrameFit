using FrameFit.Core.Geometry;

namespace FrameFit.Core.Profiles;

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
            ReserveWorkArea = Options.ReserveWorkArea
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
