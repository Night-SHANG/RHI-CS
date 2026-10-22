using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Services;

namespace Rhi.App.Presentation;

public sealed partial class PageContext(AppSession session, WindowsIntegration windows, FrameworkElement root, Action refresh)
{
    public AppSession Session { get; } = session;
    public WindowsIntegration Windows { get; } = windows;
    public FrameworkElement Root { get; } = root;
    public Action Refresh { get; } = refresh;
    public Guid? GameId { get; set; }
    public Guid? LibraryId { get; set; }
    public string GameQuery { get; set; } = "";
    public string LibraryQuery { get; set; } = "";
    public OfflineDraft Offline { get; set; } = OfflineDraft.Empty;
    public string SettingsSection { get; set; } = "General";
    public string? ThemeDraft { get; set; }
    public string? LanguageDraft { get; set; }
    public Dictionary<string, double> SettingsOffsets { get; } = [];
    public async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            await Message("Error", ex is Rhi.Core.Models.DeploymentException de
                ? Strings.Get(de.Code) + "\n" + de.Message : ex.Message);
        }
    }
    public Button Button(string key, Func<Task> action, bool enabled = true)
    {
        var button = new Button { IsEnabled = enabled, MinHeight = 36, Padding = new Thickness(12, 7, 12, 7) };
        Ui.ButtonContent(button, Strings.Get(key));
        button.Click += async (_, _) => await Run(action);
        return button;
    }
    public Button Action(string key, Action action, bool enabled = true) => Button(key, () => { action(); return Task.CompletedTask; }, enabled);
    private ScrollViewer DialogContent(UIElement content)
    {
        var scroll = new ScrollViewer
        {
            Content = content, Width = Math.Clamp(Root.ActualWidth - 96, 120, 480),
            MaxHeight = Math.Clamp(Root.ActualHeight - 220, 80, 480),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetAutomationId(scroll, "DialogContentScroll");
        return scroll;
    }
    public Task Message(string title, string text) => Show(title, Ui.Text(text));
    public async Task Show(string title, UIElement content, Action<ContentDialog>? configure = null, bool stableContent = false)
    {
        var scroll = DialogContent(content);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = Strings.Get(title),
            Content = scroll, CloseButtonText = Strings.Get("Close"),
            RequestedTheme = Root.RequestedTheme };
        TrackDialogSize(dialog, scroll, stableContent: stableContent);
        configure?.Invoke(dialog);
        await dialog.ShowAsync();
    }
    public async Task<bool> Confirm(string title, UIElement content, string primary = "Save", bool enabled = true,
        Action<ContentDialog>? configure = null, bool stableContent = false)
    {
        var scroll = DialogContent(content);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = Strings.Get(title),
            Content = scroll,
            PrimaryButtonText = Strings.Get("Cancel"), SecondaryButtonText = Strings.Get(primary),
            IsSecondaryButtonEnabled = enabled, DefaultButton = ContentDialogButton.Secondary,
            RequestedTheme = Root.RequestedTheme };
        TrackDialogSize(dialog, scroll, stableContent: stableContent);
        configure?.Invoke(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Secondary;
    }
}
