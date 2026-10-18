using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rhi.App.Presentation;

public static partial class Ui
{
    public static Grid Actions(double width, params FrameworkElement[] buttons)
    {
        var row = Row(buttons);
        row.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (var column in row.ColumnDefinitions) column.Width = new GridLength(width);
        return row;
    }
}
