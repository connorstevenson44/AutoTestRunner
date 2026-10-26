using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AutoTestRunner.Cli;

/// <summary>
/// What went wrong in autotest itself, rather than in a test: the launcher, a run, the builder, a clean up or the
/// command line. Each is written to errors.log in one logs folder, %LOCALAPPDATA%\AutoTestRunner\logs, with where it
/// happened, so a crash can be looked into; the launcher's logs button opens the folder. A mistake on a window's thread
/// is shown and the window carries on; one on another thread ends autotest, as .NET does, once it is written down.
/// </summary>
internal static class Crashes
{
    // A few are shown and carried on from; a mistake that keeps coming back, such as one in drawing a window, ends it.
    private const int Shown = 5;

    // errors.log is moved to errors.old.log once it is this big, in bytes, so it never grows without end.
    private const long Biggest = 2 * 1024 * 1024;

    private static readonly Lock Writing = new();
    private static int _shown;
    private static bool _watching;

    /// <summary>The logs folder: %LOCALAPPDATA%\AutoTestRunner\logs.</summary>
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTestRunner", "logs");

    /// <summary>Where they are written, in the logs folder.</summary>
    public static string LogFile { get; } = Path.Combine(Folder, "errors.log");

    /// <summary>Catches what no thread does, for every command; with app, also what its windows don't.</summary>
    public static void Watch(Application? app = null)
    {
        if (app is not null)
            app.DispatcherUnhandledException += OnWindowThread;
        if (_watching)
            return;
        _watching = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log(ex, "It ended autotest.");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Writes it to errors.log and the console, with what autotest was doing, such as the run whose run.log has the
    /// rest. It never throws: there is nothing more to do if it can't.
    /// </summary>
    public static void Log(Exception ex, string? doing = null)
    {
        var thread = Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} autotest {typeof(Crashes).Assembly.GetName().Version}, on thread {thread}{Environment.NewLine}"
            + (doing is null ? "" : doing + Environment.NewLine) + $"{ex}{Environment.NewLine}{Environment.NewLine}";
        try
        {
            Console.Error.Write(text);
        }
        catch (IOException)
        {
            // There is no console to write to.
        }
        lock (Writing)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > Biggest)
                    File.Move(LogFile, Path.Combine(Folder, "errors.old.log"), overwrite: true);
                File.AppendAllText(LogFile, text);
            }
            catch (Exception ignored) when (ignored is IOException or UnauthorizedAccessException)
            {
                // The console has it, where there is one.
            }
        }
    }

    /// <summary>Opens the logs folder in Explorer, made first if nothing has been written yet.</summary>
    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Folder } })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show($"Couldn't open {Folder}: {ex.Message}", "AutoTestRunner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OnWindowThread(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        if (++_shown > Shown)
            return;
        e.Handled = true;
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        var message = $"Something went wrong in AutoTestRunner itself: {e.Exception.Message}\n\nWhat happened is in errors.log in {Folder}. Open the folder?\n\nClose the window you were using if it no longer works.";
        var answer = owner is null
            ? MessageBox.Show(message, "AutoTestRunner", MessageBoxButton.YesNo, MessageBoxImage.Error)
            : MessageBox.Show(owner, message, "AutoTestRunner", MessageBoxButton.YesNo, MessageBoxImage.Error);
        if (answer == MessageBoxResult.Yes)
            OpenFolder();
    }
}
