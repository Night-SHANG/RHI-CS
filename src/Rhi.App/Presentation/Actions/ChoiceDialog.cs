using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rhi.App.Localization;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    public async Task<ContentDialogResult> Choose(string title, UIElement content, string primary, string secondary,
        string close = "Cancel", bool primaryEnabled = true, bool secondaryEnabled = true,
        string? primaryId = null, string? secondaryId = null, Action<ContentDialog>? configure = null)
    {
        var scroll = DialogContent(content);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = Strings.Get(title), Content = scroll,
            PrimaryButtonText = Strings.Get(primary), SecondaryButtonText = Strings.Get(secondary),
            CloseButtonText = Strings.Get(close), IsPrimaryButtonEnabled = primaryEnabled,
            IsSecondaryButtonEnabled = secondaryEnabled, RequestedTheme = Root.RequestedTheme,
            DefaultButton = close == "Close" ? ContentDialogButton.None : ContentDialogButton.Primary };
        TrackDialogSize(dialog, scroll);
        // WinUI can recreate native footer buttons after Opened (theme/language/template updates).
        // Keep semantic IDs on the current template instances, without replacing their styles.
        EventHandler<object> identify = (_, _) => Identify(dialog);
        dialog.LayoutUpdated += identify;
        dialog.Closed += (_, _) => dialog.LayoutUpdated -= identify;
        void Identify(DependencyObject parent)
        {
            if (parent is Button button)
            {
                if (button.Name == "PrimaryButton" && primaryId is not null && AutomationProperties.GetAutomationId(button) != primaryId)
                    AutomationProperties.SetAutomationId(button, primaryId);
                if (button.Name == "SecondaryButton" && secondaryId is not null && AutomationProperties.GetAutomationId(button) != secondaryId)
                    AutomationProperties.SetAutomationId(button, secondaryId);
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Identify(VisualTreeHelper.GetChild(parent, i));
        }
        configure?.Invoke(dialog);
        return await dialog.ShowAsync();
    }
}
