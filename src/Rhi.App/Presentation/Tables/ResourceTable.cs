using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace Rhi.App.Presentation;

public static partial class Ui
{
    public static TextBlock OneLine(string text, bool secondary = false)
    {
        var label = secondary ? Secondary(text) : Text(text);
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(label, text);
        return label;
    }

    // All rows use the same width breakpoint and reserved tracks, regardless of deployment state.
    public static Grid ResourceTableRow(bool header, bool selection, params FrameworkElement[] cells)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12), Height = header ? 48 : 80 };
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = new GridLength(0) });
        var widths = selection ? new[] { 48d, 1.1, 2.2, 1.2, 352d } : new[] { 1.2, 2.2, 1d, 352d };
        foreach (var width in widths)
            grid.ColumnDefinitions.Add(new() { Width = width >= 48 ? new GridLength(width) : new GridLength(width, GridUnitType.Star) });
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            cells[i].VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(cells[i]);
        }
        if (selection) cells[0].HorizontalAlignment = HorizontalAlignment.Center;
        var last = cells.Length - 1;
        grid.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 940;
            grid.Height = header ? 48 : narrow ? 124 : 80;
            grid.RowSpacing = narrow && !header ? 8 : 0;
            grid.RowDefinitions[0].Height = narrow && !header ? new GridLength(56) : new GridLength(1, GridUnitType.Star);
            grid.RowDefinitions[1].Height = narrow && !header ? new GridLength(36) : new GridLength(0);
            grid.ColumnDefinitions[last].Width = new GridLength(narrow ? 0 : 352);
            cells[last].Visibility = header && narrow ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetRow(cells[last], narrow ? 1 : 0);
            Grid.SetColumn(cells[last], narrow ? (selection ? 1 : 0) : last);
            Grid.SetColumnSpan(cells[last], narrow ? cells.Length - (selection ? 1 : 0) : 1);
        };
        return grid;
    }

    public static Grid CompactActions(params Button[] buttons)
    {
        foreach (var button in buttons)
        {
            button.Height = 36;
            button.Padding = new Thickness(6, 3, 6, 3);
        }
        return Actions(112, buttons);
    }
}
