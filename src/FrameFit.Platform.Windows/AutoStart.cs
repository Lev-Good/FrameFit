using Microsoft.Win32;

namespace FrameFit.Platform.Windows;

/// <summary>הפעלה אוטומטית עם עליית Windows (הרצת משתמש, ללא שירות וללא הרשאות).</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FrameFit";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool TrySet(bool enabled, out string error)
    {
        error = string.Empty;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
            {
                error = "לא ניתן לפתוח את מפתח ההפעלה האוטומטית ברישום.";
                return false;
            }

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path))
                {
                    error = "לא ניתן לקבוע את נתיב התוכנית.";
                    return false;
                }

                key.SetValue(ValueName, $"\"{path}\" --tray");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"עדכון ההפעלה האוטומטית נכשל: {ex.Message}";
            return false;
        }
    }
}
