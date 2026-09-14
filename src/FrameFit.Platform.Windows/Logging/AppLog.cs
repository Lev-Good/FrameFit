using FrameFit.Core;

namespace FrameFit.Platform.Windows.Logging;

/// <summary>לוג טקסטואלי פשוט לפתרון תקלות מרחוק. אינו מכיל מידע אישי.</summary>
public sealed class AppLog
{
    private readonly object _sync = new();
    private readonly string _path;

    public AppLog(string? path = null)
    {
        _path = path ?? AppPaths.LogFile;
    }

    public event Action<string>? LineWritten;

    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";

        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                // מגבילים את גודל הלוג כדי שלא יתפוס מקום לאורך שנים.
                if (File.Exists(_path) && new FileInfo(_path).Length > 512 * 1024)
                {
                    File.Move(_path, _path + ".old", overwrite: true);
                }

                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // לוג שנכשל לא אמור להפיל את התוכנית.
            }
        }

        LineWritten?.Invoke(line);
    }
}
