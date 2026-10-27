using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace OrderDesk;

/// <summary>Which Order Desk this is, and a link to what changed in it.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow(string shop)
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        VersionText.Text = $"Version {version.Split('+')[0]}";
        ShopText.Text = shop;
    }

    private void OnNotes(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnOk(object sender, RoutedEventArgs e) => Close();
}
