using System.IO;
using System.Windows;

namespace OrderDesk;

/// <summary>The Activity tab: activity.log, newest first, a page of ten lines at a time.</summary>
public partial class MainWindow
{
    private const int PageSize = 10;
    private int _page;

    private void ShowActivity()
    {
        var lines = File.Exists(_store.LogFile) ? File.ReadAllLines(_store.LogFile).Reverse().ToList() : [];
        var pages = Math.Max(1, (lines.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        ActivityList.ItemsSource = lines.Skip(_page * PageSize).Take(PageSize).ToList();
        PageText.Text = $"Page {_page + 1} of {pages}";
        OlderButton.IsEnabled = _page < pages - 1;
        NewerButton.IsEnabled = _page > 0;
    }

    private void OnOlder(object sender, RoutedEventArgs e)
    {
        _page++;
        ShowActivity();
    }

    private void OnNewer(object sender, RoutedEventArgs e)
    {
        _page--;
        ShowActivity();
    }
}
