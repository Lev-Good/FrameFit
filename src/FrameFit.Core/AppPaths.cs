namespace FrameFit.Core;

/// <summary>מיקומי הקבצים של FrameFit.</summary>
public static class AppPaths
{
    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FrameFit");

    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public static string LogFile => Path.Combine(ConfigDirectory, "framefit.log");
}
