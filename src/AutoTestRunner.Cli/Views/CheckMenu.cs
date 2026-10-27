using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AutoTestRunner.Building;

namespace AutoTestRunner.Cli.Views;

/// <summary>The menu of every check, under its group, each with the group's icon, that opens a check's form.</summary>
internal static class CheckMenu
{
    /// <summary>The icon of a group of checks.</summary>
    public static string Glyph(string group) => group switch
    {
        "In the app" => Icons.Look,
        "Files" => Icons.Document,
        "Windows" => Icons.App,
        "Data" => Icons.Database,
        _ => Icons.Values,
    };

    /// <summary>Opens the menu under a button; chosen is called with the check picked. only limits it to some checks.</summary>
    public static void Open(UIElement under, Action<CheckKind> chosen, IReadOnlySet<string>? only = null)
    {
        var menu = new ContextMenu { PlacementTarget = under, Placement = PlacementMode.Bottom };
        var kinds = Forms.CheckKinds.Where(kind => only is null || only.Contains(kind.Key)).ToList();
        foreach (var group in kinds.GroupBy(kind => kind.Group))
        {
            // One group is listed as it is; more are each a menu of their own.
            var items = menu.Items;
            if (kinds.Select(kind => kind.Group).Distinct().Count() > 1)
            {
                var groupItem = new MenuItem { Header = group.Key, Icon = Ui.Glyph(Glyph(group.Key), 14, Ui.Accent) };
                menu.Items.Add(groupItem);
                items = groupItem.Items;
            }
            foreach (var kind in group)
            {
                var item = new MenuItem { Header = kind.Title, Icon = Ui.Glyph(Icons.CheckList, 13, Ui.Pass) };
                item.Click += (_, _) => chosen(kind);
                items.Add(item);
            }
        }
        menu.IsOpen = true;
    }
}
