using System.Windows;
using System.Windows.Threading;
using Throng.App.Services;
using Throng.App.Views;

namespace Throng.App;

/// <summary>
/// Application entry: sets up logging, the global exception boundary and the retro font, then shows the main window.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Exception boundary: anything unhandled on the UI thread is logged and reported in a friendly dialog instead
    /// of crashing (no exceptions surfaced raw to the user).
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.Initialise();
        AppLog.Info($"Throng starting, version {typeof(App).Assembly.GetName().Version}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        RetroFont.Apply(Resources);
        base.OnStartup(e);

        string? startupFile = e.Args.FirstOrDefault(a => a.EndsWith(Throng.IO.ThrongFile.Extension, StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(startupFile);
        MainWindow = window;
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI exception", e.Exception);
        string message =
            $"Something went wrong and Throng recovered:\n\n{e.Exception.Message}\n\nDetails were written to the log:\n{AppPaths.LogFile}";
        e.Handled = true;

        try
        {
            // Themed dialog once the main window is up (D-029).
            ThrongDialog.ShowMessage(Current?.MainWindow, "Throng recovered", message, DialogTone.Warning,
                [new DialogButton("OK", IsDefault: true, IsCancel: true)]);
        }
        catch (Exception dialogFailure)
        {
            // The theme or window may be what failed (e.g. during startup), so fall back to the stock box.
            AppLog.Error("Themed error dialog failed", dialogFailure);
            MessageBox.Show(message, "Throng", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
