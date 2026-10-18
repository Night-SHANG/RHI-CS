using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rhi.App.Localization;

namespace Rhi.App.Presentation;

public static class Ui
{
    public static ElementTheme CurrentTheme { get; set; } = ElementTheme.Light;
    public static Brush ThemeBrush(string key)
    {
        var dictionary = Application.Current.Resources.MergedDictionaries.Last();
        var theme = (ResourceDictionary)dictionary.ThemeDictionaries[CurrentTheme == ElementTheme.Dark ? "Dark" : "Light"];
        return (Brush)theme[key];
    }
    public static TextBlock Text(string text, double size = 14) => new()
    { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    public static TextBlock Label(string key, double size = 14) => Text(Strings.Get(key), size);
    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static StackPanel Row(params UIElement[] children)
    {
        var panel = Stack(children); panel.Orientation = Orientation.Horizontal; return panel;
    }
    public static Grid Columns(double[] widths, params FrameworkElement[] cells)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12), MinHeight = 58 };
        foreach (var width in widths) grid.ColumnDefinitions.Add(new() { Width = new GridLength(width, GridUnitType.Star) });
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return grid;
    }
    public static Border Card(UIElement content) => new()
    { Padding = new Thickness(18), BorderThickness = new Thickness(1), BorderBrush = ThemeBrush("LineBrush"),
        Background = ThemeBrush("CardBrush"), CornerRadius = new CornerRadius(8), Child = content };
    public static TextBox Input(string key, string value = "") => new()
    { Header = Strings.Get(key), Text = value, MinWidth = 250, TextWrapping = TextWrapping.Wrap };
    public static ComboBox Choice(string key, IEnumerable<string> values, int selected = 0)
    {
        var box = new ComboBox { Header = Strings.Get(key), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 170 };
        foreach (var value in values) box.Items.Add(value);
        box.SelectedIndex = selected;
        return box;
    }
    public static ScrollViewer Scroll(UIElement child) => new()
    { Content = child, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public static Grid Split(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var sidebar = new Border { Padding = new Thickness(20), BorderThickness = new Thickness(0, 0, 1, 0), Child = left };
        sidebar.Background = ThemeBrush("PanelBrush");
        sidebar.BorderBrush = ThemeBrush("LineBrush");
        var content = new Border { Padding = new Thickness(28), Child = right };
        Grid.SetColumn(content, 1); grid.Children.Add(sidebar); grid.Children.Add(content);
        return grid;
    }
    public static Grid Dock(UIElement body, UIElement footer)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(body);
        var border = new Border { Padding = new Thickness(0, 16, 0, 0), Child = footer };
        Grid.SetRow(border, 1); grid.Children.Add(border); return grid;
    }
}
