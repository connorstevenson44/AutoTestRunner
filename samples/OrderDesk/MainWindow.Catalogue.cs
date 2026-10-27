using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OrderDesk;

/// <summary>
/// The Catalogue tab: categories in a tree, the products in the one picked, and a product dragged onto the order form
/// to order it. The drag follows the mouse itself rather than Windows' drag and drop, so it works however the mouse moves.
/// </summary>
public partial class MainWindow
{
    private Product? _dragging;
    private Point _dragFrom;
    private bool _dragMoved;

    private void ShowCategories()
    {
        foreach (var top in Product.All.Select(product => product.Category.Split('/')[0]).Distinct())
        {
            var item = new TreeViewItem { Header = top, Tag = top };
            foreach (var sub in Product.All.Where(product => product.Category.StartsWith(top + "/", StringComparison.Ordinal))
                         .Select(product => product.Category).Distinct())
                item.Items.Add(new TreeViewItem { Header = sub[(top.Length + 1)..], Tag = sub });
            CategoryTree.Items.Add(item);
        }
    }

    private void OnCategory(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: string category })
            return;
        CatalogueList.ItemsSource = Product.All.Where(product => product.Category == category || product.Category.StartsWith(category + "/", StringComparison.Ordinal)).ToList();
        CatalogueList.ScrollIntoView(CatalogueList.Items.Count > 0 ? CatalogueList.Items[0] : null);
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragging = (e.OriginalSource as FrameworkElement)?.DataContext as Product;
        _dragFrom = e.GetPosition(this);
        _dragMoved = false;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragging = null;
            return;
        }
        var moved = e.GetPosition(this) - _dragFrom;
        if (!_dragMoved && (Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance))
        {
            _dragMoved = true;
            Mouse.OverrideCursor = Cursors.Hand;
        }
    }

    private void OnDragDrop(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is not { } product)
            return;
        (_dragging, Mouse.OverrideCursor) = (null, null);
        var at = e.GetPosition(OrderForm);
        if (!_dragMoved || at.X < 0 || at.Y < 0 || at.X > OrderForm.ActualWidth || at.Y > OrderForm.ActualHeight)
            return;
        if (product.IsSparePart)
        {
            Status($"{product.Name} is a spare part: the service desk orders those.");
            return;
        }
        ProductBox.SelectedItem = product.Name;
        Status($"Picked {product.Name} from the catalogue.");
    }
}
