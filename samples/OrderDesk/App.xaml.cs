using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace OrderDesk;

/// <summary>
/// Opens the sign-in window, then the main window once someone has signed in. With --export it writes the export and
/// ends without a window, as the nightly export's scheduled task runs it.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Order Desk is a UK shop: dates are written 03/11/2026 whatever the computer's own settings.
        var british = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = british;
        Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = british;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(british.IetfLanguageTag)));

        var connections = Connections.Beside(AppContext.BaseDirectory);
        var store = new OrderStore(DataFolder(e.Args), connections.Orders);
        Pricing.Settings = ShopSettings.Load(store.SettingsFile);
        if (e.Args.Contains("--export"))
        {
            var file = Exports.Write(store, Exports.Folder(store));
            store.Log($"Nightly export wrote {file}");
            Shutdown(0);
            return;
        }
        new SignInWindow(store, connections).Show();
    }

    // --data <folder>, or Order Desk in the local app data folder, which the demo suites look in.
    private static string DataFolder(string[] args)
    {
        var index = Array.IndexOf(args, "--data");
        if (index >= 0 && index + 1 < args.Length)
            return Path.GetFullPath(args[index + 1]);
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } given
            ? given
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "OrderDesk");
    }
}
