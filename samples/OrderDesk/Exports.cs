using System.IO;

namespace OrderDesk;

/// <summary>Writing every order to orders.csv, in the folder picked for exports, else the data folder.</summary>
public static class Exports
{
    public const string FileName = "orders.csv";

    public static string Folder(OrderStore store) => Remembered.ExportFolder is { Length: > 0 } folder ? folder : store.Folder;

    /// <summary>Writes the export in one go, as the nightly export does, and gives its path.</summary>
    public static string Write(OrderStore store, string folder)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, FileName);
        File.WriteAllLines(file, [OrderStore.CsvHeader, .. store.Orders().Select(OrderStore.CsvLine)]);
        return file;
    }
}
