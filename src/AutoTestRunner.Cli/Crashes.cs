using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AutoTestRunner.Cli;

/// <summary>
/// What went wrong in autotest itself, rather than in a test: written to errors.log with where it happened, so a crash
/// can be looked into. A mistake on a window's thread is shown and the window carries on; one on another thread ends
/// autotest, as .NET does, once it is written down.
/// </summary>
internal static class Crashes
{
    // A few are shown and carried on from; a mistake that keeps coming back, such as one in drawing a window, ends it.
    private const int Shown = 5;

    private static int _shown;

    /// <summary>Where they are written: %LOCALAPPDATA%\AutoTestRunner\errors.log.</summary>
    public static string LogFile { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTestRunner", "errors.log");

    /// <summary>Catches what the windows of this app don't, and what other threads don't.</summary>
    public static void Watch(Application app)
    {
        app.DispatcherUnhandledException += OnWindowThread;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Writes it to errors.log and the console. It never throws: there is nothing more to do if it can't.</summary>
    public static void Log(Exception ex)
    {
        var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} autotest {typeof(Crashes).Assembly.GetName().Version}, on thread {Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString(System.Globalization.CultureInfo.InvariantCulture)}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
        try
        {
            Console.Error.Write(text);
        }
        catch (IOException)
        {
            // There is no console to write to.
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, text);
        }
        catch (Exception ignored) when (ignored is IOException or UnauthorizedAccessException)
        {
            // The console has it, where there is one.
        }
    }

    private static void OnWindowThread(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        if (++_shown > Shown)
            return;
        e.Handled = true;
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        var message = $"Something went wrong in AutoTestRunner itself: {e.Exception.Message}\n\nWhat happened is in {LogFile}. Close the window you were using if it no longer works.";
        if (owner is null)
            MessageBox.Show(message, "AutoTestRunner", MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(owner, message, "AutoTestRunner", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
