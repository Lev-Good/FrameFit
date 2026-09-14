using System;
using System.Drawing;
using System.Windows.Forms;

namespace FrameFit.App.Tray;

/// <summary>
/// סמל בסרגל המערכת. "שחזר הכול" כאן הוא מנגנון הבטיחות הנגיש תמיד —
/// גם אם חלון הניהול נסגר או שהעכבר חסום לאזור הגלוי.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public TrayIcon(Action open, Action revertAll, Action exit)
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("פתח את FrameFit");
        openItem.Click += (_, _) => open();
        menu.Items.Add(openItem);

        var revertItem = new ToolStripMenuItem("שחזר הכול למצב Windows");
        revertItem.Click += (_, _) => revertAll();
        menu.Items.Add(revertItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("יציאה");
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "FrameFit — התאמת אזור התצוגה למסגרת",
            Visible = true,
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => open();
    }

    public void ShowInfo(string title, string text)
    {
        if (_disposed)
        {
            return;
        }

        _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
