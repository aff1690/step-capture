using System.Windows;
using System.Windows.Threading;
using StepCapture.App.Views;

namespace StepCapture.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Log.Error("Unhandled", ex);
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // Opening a .stepguide file by double-click or drag onto the exe.
        if (e.Args.Length > 0 && File.Exists(e.Args[0])) window.OpenFile(e.Args[0]);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI", e.Exception);
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to {Log.FilePath}",
            "StepCapture", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
