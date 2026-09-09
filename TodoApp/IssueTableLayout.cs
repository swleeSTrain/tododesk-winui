using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TodoApp;

// Shared by both theme surfaces; respond to table space, not the outer window.
internal static class IssueTableLayout
{
    public static void Update(Grid? grid, double ownerWidth, double dueWidth)
    {
        if (grid is null || grid.ColumnDefinitions.Count != 5) return;
        var compact = grid.ActualWidth < 620;
        grid.ColumnDefinitions[3].Width = new GridLength(compact ? 0 : ownerWidth);
        grid.ColumnDefinitions[4].Width = new GridLength(compact ? 0 : dueWidth);
        grid.ColumnSpacing = compact ? 8 : 12;
        foreach (var child in grid.Children.OfType<FrameworkElement>())
            if (Grid.GetColumn(child) >= 3)
                child.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }
}
