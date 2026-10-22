using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rhi.App.Presentation;

public static partial class Ui
{
    public static Grid DirectoryField(TextBox path, params Button[] buttons)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(path);
        foreach (var button in buttons)
        {
            Grid.SetColumn(button, row.ColumnDefinitions.Count);
            row.ColumnDefinitions.Add(new() { Width = new GridLength(112) });
            button.Height = 36;
            button.Padding = new Thickness(6, 3, 6, 3);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.VerticalAlignment = VerticalAlignment.Bottom;
            row.Children.Add(button);
        }
        return row;
    }
}
