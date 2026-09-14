using System.Text;

namespace FrameFit.Core.Profiles;

/// <summary>שיוך פרופיל לתצוגה נוכחית, והוספה/עדכון של פרופילים.</summary>
public static class ProfileMatcher
{
    /// <summary>
    /// מחזיר את הפרופיל המתאים ביותר לתצוגה, או null אם אין התאמה מספקת.
    /// התאמה חלקית מדי לעולם אינה מוחלת — עדיף לא להחיל מאשר להחיל הגדרה שגויה.
    /// </summary>
    public static FrameFitProfile? FindBest(SettingsDocument document, DisplayFingerprint display)
    {
        FrameFitProfile? best = null;
        var bestScore = 0;

        foreach (var profile in document.Profiles)
        {
            if (!profile.Display.IsAcceptableMatch(display))
            {
                continue;
            }

            var score = profile.Display.MatchScore(display);
            if (score > bestScore)
            {
                best = profile;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>מוסיף פרופיל חדש או מעדכן פרופיל קיים לפי מזהה.</summary>
    public static void Upsert(SettingsDocument document, FrameFitProfile profile)
    {
        var index = document.Profiles.FindIndex(p =>
            string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            document.Profiles[index] = profile;
        }
        else
        {
            document.Profiles.Add(profile);
        }
    }

    /// <summary>מייצר מזהה פרופיל יציב מתוך טביעת האצבע של התצוגה.</summary>
    public static string BuildId(DisplayFingerprint display)
    {
        var seed = string.IsNullOrEmpty(display.EdidHash)
            ? display.DeviceName
            : display.EdidHash;

        var raw = $"{seed}-{display.OutputTechnology}-{display.Width}x{display.Height}";

        // החלפת כל תו לא-בטוח במקף, וצמצום מקפים רצופים — כדי שמזהה המזהה יהיה קריא ויציב.
        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            var mapped = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-';
            if (mapped == '-' && builder.Length > 0 && builder[^1] == '-')
            {
                continue;
            }

            builder.Append(mapped);
        }

        var safe = builder.ToString().Trim('-').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(safe) ? "default" : safe;
    }
}
