using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Rhi.App.Presentation;

public static partial class Ui
{
    public static TextBlock Secondary(string text)
    {
        var label = Text(text);
        label.Foreground = ThemeBrush("SecondaryTextBrush");
        return label;
    }

    public static Grid Status(string key, bool singleLine = false)
    {
        var brush = key switch
        {
            "Installed" or "Available" or "TargetMatched" or "OfflineReady" => "SuccessBrush",
            "SourceUnavailable" or "NeedsRepair" or "TargetModified" or "TargetMissing" or "TargetUnavailable" => "WarningBrush",
            _ => "SecondaryTextBrush"
        };
        var icon = new FontIcon { Glyph = "\uE915", FontSize = 10, Foreground = ThemeBrush(brush),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        var text = Label(key);
        if (singleLine)
        {
            text.TextWrapping = TextWrapping.NoWrap;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(text, text.Text);
        }
        text.Foreground = ThemeBrush(brush);
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1); grid.Children.Add(icon); grid.Children.Add(text);
        return grid;
    }

    public static Border TableLine(UIElement content, bool header = false) => new()
    {
        Child = content, BorderThickness = new Thickness(1, header ? 1 : 0, 1, 1),
        BorderBrush = ThemeBrush("LineBrush"), Background = ThemeBrush(header ? "PanelBrush" : "CardBrush")
    };

    public static void ButtonContent(Button button, string text)
    {
        // Let the native button template own foreground/disabled/hover colors.
        button.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center };
        AutomationProperties.SetName(button, text);
        button.MinWidth = 0;
    }
}
