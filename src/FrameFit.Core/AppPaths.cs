namespace FrameFit.Core;

/// <summary>מיקומי הקבצים של FrameFit.</summary>
public static class AppPaths
{
    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FrameFit");

    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public static string LogFile => Path.Combine(ConfigDirectory, "framefit.log");

    /// <summary>
    /// סימון שנכתב כל עוד סרגל המשימות של Windows מוסתר ע"י FrameFit, ונמחק כשמחזירים
    /// אותו. אם התהליך נהרג — הסימון נשאר ומאפשר להחזיר את הסרגל בעלייה הבאה.
    /// </summary>
    public static string TaskbarMarkerFile => Path.Combine(ConfigDirectory, "taskbar-hidden.marker");
}
