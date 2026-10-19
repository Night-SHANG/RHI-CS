using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;

namespace Rhi.App.Pages;

public sealed partial class SettingsView : UserControl
{
    private readonly PageContext c;
    public SettingsView(PageContext c)
    {
        this.c = c;
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch, Margin = new Thickness(40, 28, 36, 28) };
        var navigation = new NavigationView { IsSettingsVisible = false, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsPaneToggleButtonVisible = false, OpenPaneLength = 240, PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsPaneOpen = true, PaneTitle = Strings.Get("Settings"), Content = host };
        AutomationProperties.SetAutomationId(navigation, "SettingsNavigation");
        var pages = new Dictionary<string, UIElement>();
        var sections = new[] { ("General", "General"), ("Data", "ApplicationData"),
            ("Protection", "ProtectedCopies"), ("Offline", "OfflineSection") };
        foreach (var (id, key) in sections)
        {
            var item = new NavigationViewItem { Content = Strings.Get(key), Tag = id };
            AutomationProperties.SetAutomationId(item, "SettingsNav" + id);
            navigation.MenuItems.Add(item);
        }
        var license = new NavigationViewItem { Content = Strings.Get("OpenLicenseFolder"), Tag = "License" };
        navigation.FooterMenuItems.Add(license);
        void Select(string id)
        {
            c.SettingsSection = id;
            if (!pages.TryGetValue(id, out var page))
            {
                page = id switch
                {
                    "Protection" => ProtectionPage(),
                    "Offline" => ScrollPage(id, OfflinePage()),
                    "Data" => ScrollPage(id, DataPage()),
                    _ => ScrollPage(id, GeneralPage())
                };
                pages.Add(id, page);
            }
            host.Content = page;
        }
        navigation.SelectionChanged += (_, e) =>
        {
            if (e.SelectedItem is NavigationViewItem { Tag: string id } && id != "License") Select(id);
        };
        navigation.ItemInvoked += async (_, e) =>
        {
            if (e.InvokedItemContainer == license)
            {
                await c.Run(() => { WindowsIntegration.OpenFolder(AppContext.BaseDirectory); return Task.CompletedTask; });
                navigation.SelectedItem = navigation.MenuItems.Cast<NavigationViewItem>().First(i => (string)i.Tag == c.SettingsSection);
            }
        };
        navigation.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 1000;
            navigation.PaneDisplayMode = narrow ? NavigationViewPaneDisplayMode.Top : NavigationViewPaneDisplayMode.Left;
            navigation.IsPaneOpen = !narrow;
            host.Margin = narrow ? new Thickness(24, 20, 24, 20) : new Thickness(40, 28, 36, 28);
        };
        Content = navigation;
        navigation.SelectedItem = navigation.MenuItems.Cast<NavigationViewItem>().First(i => (string)i.Tag == c.SettingsSection);
        if (host.Content is null) Select(c.SettingsSection);
    }

    private ScrollViewer ScrollPage(string id, FrameworkElement body)
    {
        body.MaxWidth = 920;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        var scroll = Ui.Scroll(body);
        scroll.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        scroll.SizeChanged += (_, e) => body.Width = Math.Min(920, e.NewSize.Width);
        scroll.ViewChanged += (_, _) => c.SettingsOffsets[id] = scroll.VerticalOffset;
        scroll.Loaded += (_, _) => scroll.ChangeView(null, c.SettingsOffsets.GetValueOrDefault(id), null, true);
        AutomationProperties.SetAutomationId(scroll, "SettingsScroll");
        return scroll;
    }
}
