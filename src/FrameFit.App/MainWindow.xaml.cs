using System;
using System.ComponentModel;
using System.Windows;
using FrameFit.App.Tray;
using FrameFit.App.ViewModels;

namespace FrameFit.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TrayIcon _tray;
    private bool _exiting;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;

        _tray = new TrayIcon(
            open: ShowFromTray,
            revertAll: () => Dispatcher.Invoke(() => _viewModel.RevertCommand.Execute(null)),
            exit: ExitApplication);

        _tray.ShowInfo(
            "FrameFit פועל",
            "התוכנה נשארת בסרגל המערכת. סגירת החלון אינה מבטלת את ההגדרה.");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            // סגירת החלון משאירה את FrameFit פעיל בסרגל המערכת, כדי שהאכיפה תימשך.
            e.Cancel = true;
            Hide();
            _tray.ShowInfo("FrameFit ממשיך לפעול", "לפתיחה מחדש לחץ פעמיים על הסמל בסרגל המערכת.");
            return;
        }

        base.OnClosing(e);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitClicked(object sender, RoutedEventArgs e) => ExitApplication();

    private void ExitApplication()
    {
        _exiting = true;
        _tray.Dispose();
        Application.Current.Shutdown();
    }
}
