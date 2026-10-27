using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace OrderDesk;

/// <summary>Export, back up and restore, import, and the nightly export's scheduled task.</summary>
public partial class MainWindow
{
    private const string NightlyTask = "Order Desk nightly export";

    private async void OnExport(object sender, RoutedEventArgs e) => await Export(Exports.Folder(_store));

    private async void OnExportTo(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Export orders to", InitialDirectory = Exports.Folder(_store) };
        if (picker.ShowDialog(this) != true)
        {
            Status("Export cancelled.");
            return;
        }
        Remembered.ExportFolder = picker.FolderName;
        await Export(picker.FolderName);
    }

    // Writes a line at a time, slowly, as a real export to a slow disk or server would, saying how far it has got.
    private async Task Export(string folder)
    {
        var orders = _store.Orders();
        if (orders.Count == 0)
        {
            Status("There are no orders to export.");
            return;
        }
        ExportButton.IsEnabled = false;
        var file = Path.Combine(folder, Exports.FileName);
        try
        {
            Directory.CreateDirectory(folder);
            await using var writer = new StreamWriter(file, append: false);
            await writer.WriteLineAsync(OrderStore.CsvHeader);
            for (var index = 0; index < orders.Count; index++)
            {
                Status($"Exporting order {index + 1} of {orders.Count}…");
                await Task.Delay(500);
                await writer.WriteLineAsync(OrderStore.CsvLine(orders[index]));
            }
        }
        finally
        {
            ExportButton.IsEnabled = CheckedBox.IsChecked == true;
        }
        _store.Log($"Exported {Count(orders.Count)} to {file}");
        Status($"Exported {Count(orders.Count)}. File: {file}");
    }

    private void OnBackUp(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Back up to", InitialDirectory = Exports.Folder(_store) };
        if (picker.ShowDialog(this) != true)
        {
            Status("Back up cancelled.");
            return;
        }
        var file = _store.Backup(picker.FolderName);
        Status($"Backed up {Count(_rows.Count)}. File: {file}");
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Restore a backup", Filter = "Order Desk backups (*.zip)|*.zip" };
        if (picker.ShowDialog(this) != true)
        {
            Status("Restore cancelled.");
            return;
        }
        if (OrderStore.OrdersIn(picker.FileName) is not { } count)
        {
            Status($"{Path.GetFileName(picker.FileName)} isn't an Order Desk backup.");
            return;
        }
        var answer = MessageBox.Show(this, $"Replace the {Count(_rows.Count)} here with the {Count(count)} in {Path.GetFileName(picker.FileName)}?",
            "Restore a backup", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            Status("Kept the orders as they are.");
            return;
        }
        _store.Restore(picker.FileName);
        Reload();
        Status($"Restored {Count(count)} from {picker.FileName}.");
    }

    // Asks about each order in the file in a window of its own, as some imports do: Import or Skip, one at a time.
    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Import orders", Filter = "Order files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) != true)
        {
            Status("Import cancelled.");
            return;
        }
        var lines = ImportLine.Read(picker.FileName);
        int imported = 0, skipped = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            Status($"Importing order {index + 1} of {lines.Count}…");
            // A moment between windows, as there is when an app does some work for each.
            await Task.Delay(400);
            var line = lines[index];
            var window = new ImportWindow(line, index + 1, lines.Count) { Owner = this };
            if (window.ShowDialog() == true && Product.Named(line.Product) is { IsSparePart: false } product && line.Quantity is >= 1 and <= 99)
            {
                _rows.Add(new OrderRow(_store.Add(line.Customer, product, line.Quantity, false, Delivery.Standard, "", line.Note)));
                imported++;
            }
            else
                skipped++;
        }
        _store.Log($"Imported {imported}, skipped {skipped} from {picker.FileName}");
        Status($"Import finished: {imported} imported, {skipped} skipped.");
    }

    // A task of the person's own, so it needs no administrator: Order Desk with --export, at 23:00 each night.
    private void OnSchedule(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath ?? "OrderDesk.exe";
        var (code, output) = Schtasks("/Create", "/F", "/SC", "DAILY", "/ST", "23:00", "/TN", NightlyTask, "/TR", $"\"{exe}\" --export");
        if (code != 0)
        {
            Status($"Couldn't schedule the nightly export: {output}");
            return;
        }
        _store.Log("Scheduled the nightly export at 23:00");
        Status("Scheduled the nightly export at 23:00.");
    }

    private void OnUnschedule(object sender, RoutedEventArgs e)
    {
        var (code, output) = Schtasks("/Delete", "/F", "/TN", NightlyTask);
        Status(code == 0 ? "Stopped the nightly export." : $"There is no nightly export to stop: {output}");
        if (code == 0)
            _store.Log("Stopped the nightly export");
    }

    private static (int Code, string Output) Schtasks(params string[] args)
    {
        var start = new ProcessStartInfo("schtasks.exe") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }
}
