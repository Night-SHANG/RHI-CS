using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Services;

namespace Rhi.App.Presentation;

public sealed class PageContext(AppSession session, WindowsIntegration windows, FrameworkElement root, Action refresh)
{
    public AppSession Session { get; } = session;
    public WindowsIntegration Windows { get; } = windows;
    public FrameworkElement Root { get; } = root;
    public Action Refresh { get; } = refresh;
    public Guid? GameId { get; set; }
    public Guid? LibraryId { get; set; }
    public Guid? VersionId { get; set; }
    public async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { await Message("Error", ex is Rhi.Core.Models.DeploymentException de
            ? Strings.Get(de.Code) + "\n" + de.Message : ex.Message); }
    }
    public Button Button(string key, Func<Task> action, bool enabled = true)
    {
        var button = new Button { Content = Strings.Get(key), IsEnabled = enabled, MinHeight = 36 };
        button.Click += async (_, _) => await Run(action);
        return button;
    }
    public Button Action(string key, Action action, bool enabled = true) => Button(key, () => { action(); return Task.CompletedTask; }, enabled);
    public async Task Message(string title, string text)
    {
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = Strings.Get(title),
            Content = Ui.Scroll(Ui.Text(text)), CloseButtonText = Strings.Get("Close"),
            RequestedTheme = Root.RequestedTheme };
        await dialog.ShowAsync();
    }
    public async Task<bool> Confirm(string title, UIElement content, string primary = "Save", bool enabled = true)
    {
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = Strings.Get(title),
            Content = new ScrollViewer { Content = content, MaxHeight = 480,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = Strings.Get(primary), CloseButtonText = Strings.Get("Cancel"),
            IsPrimaryButtonEnabled = enabled, DefaultButton = ContentDialogButton.Close,
            RequestedTheme = Root.RequestedTheme };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
