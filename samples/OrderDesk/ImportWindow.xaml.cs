using System.Windows;

namespace OrderDesk;

/// <summary>Asks whether to add one order from a file being imported: Import, or Skip.</summary>
public partial class ImportWindow : Window
{
    public ImportWindow(ImportLine line, int number, int count)
    {
        InitializeComponent();
        Counter.Text = $"Order {number} of {count}";
        Summary.Text = line.Summary;
    }

    private void OnImport(object sender, RoutedEventArgs e) => DialogResult = true;
}
