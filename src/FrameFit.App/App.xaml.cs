using System;
using System.Linq;
using System.Windows;
using FrameFit.App.Infrastructure;
using FrameFit.App.Services;
using FrameFit.App.ViewModels;
using FrameFit.Platform.Windows.Logging;

namespace FrameFit.App;

public partial class App : Application
{
    private FrameFitSession? _session;
    private AppLog? _log;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            ConsoleBridge.AttachToParent();
            var exitCode = SelfTest.Run(e.Args);
            Environment.ExitCode = exitCode;
            Shutdown(exitCode);
            return;
        }

        _log = new AppLog();
        _log.Write($"FrameFit מתחיל. גרסה {typeof(App).Assembly.GetName().Version}");

        _session = new FrameFitSession(_log);

        var viewModel = new MainViewModel(_session, _log);

        _window = new MainWindow(viewModel);

        // מקש החירום הגלובלי משחזר את המצב ומחזיר את חלון הניהול לחזית.
        _session.PanicRequested += () => Dispatcher.Invoke(() =>
        {
            if (_window is not null)
            {
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
            }
        });

        // אם קיימת הגדרה שמורה ומזוהה, היא מוחלת מיד — בלי מגע של המשתמש.
        var applied = viewModel.TryAutoApply();
        _log.Write(applied
            ? "ההגדרה השמורה הוחלה אוטומטית."
            : "לא הוחלה הגדרה אוטומטית (אין פרופיל תואם למסך הזה).");

        var startInTray = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (!startInTray)
        {
            _window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // תמיד מחזירים את המערכת למצב נקי ביציאה — ראו החלטה D4.
        _session?.Dispose();
        _session = null;

        _log?.Write("FrameFit הסתיים.");
        base.OnExit(e);
    }
}
