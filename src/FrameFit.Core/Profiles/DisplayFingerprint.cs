namespace FrameFit.Core.Profiles;

/// <summary>
/// טביעת אצבע חומרית של תצוגה. ההגדרה נקשרת לחומרה ולא למספר המסך או למיקומו,
/// כדי לחזור לעצמה גם אחרי החלפת יציאה או הוספת מסך (החלטה D7).
/// </summary>
public sealed record DisplayFingerprint(
    string DeviceName,
    string EdidHash,
    string OutputTechnology,
    int Width,
    int Height)
{
    /// <summary>הציון המינימלי שממנו נחשבים למסך מזוהה.</summary>
    public const int MatchThreshold = 60;

    public static DisplayFingerprint Unknown { get; } = new(string.Empty, string.Empty, string.Empty, 0, 0);

    /// <summary>
    /// מידת ההתאמה בין טביעת אצבע שמורה לבין תצוגה נוכחית. 0–100.
    /// ה-EDID הוא המזהה החזק; שם ההתקן, סוג החיבור והרזולוציה מחזקים.
    /// </summary>
    public int MatchScore(DisplayFingerprint other)
    {
        var score = 0;

        if (!string.IsNullOrEmpty(EdidHash) &&
            string.Equals(EdidHash, other.EdidHash, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (!string.IsNullOrEmpty(DeviceName) &&
            string.Equals(DeviceName, other.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (!string.IsNullOrEmpty(OutputTechnology) &&
            string.Equals(OutputTechnology, other.OutputTechnology, StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (Width == other.Width && Height == other.Height && Width > 0)
        {
            score += 10;
        }

        return score;
    }

    /// <summary>התאמה חזקה — מבוססת EDID, או ציון כולל גבוה.</summary>
    public bool IsMatch(DisplayFingerprint other) => MatchScore(other) >= MatchThreshold;

    /// <summary>
    /// התאמה שמספיק טובה כדי להחיל לפיה הגדרה. ב-Windows קוראים את ה-EDID מהרישום,
    /// ולפעמים הוא אינו זמין (דרייבר גרפי, תצוגה וירטואלית, התקן פגום). במקרה כזה
    /// מסתמכים על שם ההתקן **וגם** על הרזולוציה יחד — צירוף מזהה דיו.
    /// </summary>
    public bool IsAcceptableMatch(DisplayFingerprint other)
    {
        if (IsMatch(other))
        {
            return true;
        }

        return !string.IsNullOrEmpty(DeviceName) &&
               string.Equals(DeviceName, other.DeviceName, StringComparison.OrdinalIgnoreCase) &&
               Width == other.Width &&
               Height == other.Height &&
               Width > 0;
    }
}
