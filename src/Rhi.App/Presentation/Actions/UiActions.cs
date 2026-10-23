using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace Rhi.App.Presentation;

public static partial class Ui
{
    public static VariableSizedWrapGrid HeaderActions(string id, params Button[] buttons)
    {
        // Native wrapping owns measurement and arrangement; both page headers share these slots.
        var panel = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal, ItemWidth = 188, ItemHeight = 44,
            MaximumRowsOrColumns = Math.Max(1, buttons.Length), HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(panel, id);
        foreach (var button in buttons)
        {
            button.Width = 180;
            button.Height = 36;
            button.Margin = new Thickness(0, 0, 8, 8);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.VerticalAlignment = VerticalAlignment.Top;
            if (button.Content is TextBlock label) label.TextWrapping = TextWrapping.NoWrap;
            panel.Children.Add(button);
        }
        panel.SizeChanged += (_, e) =>
        {
            var columns = Math.Max(1, Math.Min(panel.Children.Count, (int)Math.Floor(e.NewSize.Width / 188)));
            if (panel.MaximumRowsOrColumns != columns) panel.MaximumRowsOrColumns = columns;
        };
        return panel;
    }

    public static Grid Actions(double width, params FrameworkElement[] buttons)
    {
        var row = Row(buttons);
        row.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (var column in row.ColumnDefinitions) column.Width = new GridLength(width);
        foreach (var button in buttons) button.VerticalAlignment = VerticalAlignment.Stretch;
        return row;
    }
}
