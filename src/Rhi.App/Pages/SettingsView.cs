using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;

namespace Rhi.App.Pages;

public sealed class SettingsView : UserControl
{
    public SettingsView(PageContext c)
    {
        var state = c.Session.State!;
        var themes = new[] { "Light", "Dark", "System" };
        var languages = new[] { "System", "zh-CN", "en-US" };
        var theme = Ui.Choice("Appearance", themes.Select(Strings.Get), Math.Max(0, Array.IndexOf(themes, state.Preferences.Theme)));
        var language = Ui.Choice("Language", languages.Select(Strings.Get), Math.Max(0, Array.IndexOf(languages, state.Preferences.Language)));
        theme.IsEnabled = !c.Session.Busy; language.IsEnabled = !c.Session.Busy;
        var save = c.Action("Save", () => c.Session.Save(state with { Preferences = new(themes[theme.SelectedIndex], languages[language.SelectedIndex]) }), !c.Session.Busy);
        var root = c.Session.Store!.Root;
        var general = Ui.Card(Ui.Stack(theme, language, save));
        var data = Ui.Card(Ui.Stack(Ui.Label("ApplicationData", 20), Ui.Text(root), Ui.Label("NoMigration"),
            c.Action("OpenFolder", () => WindowsIntegration.OpenFolder(root)), Ui.Label("ProtectedCopies", 20),
            Ui.Label("ProtectionRetention"), c.Action("OpenProtectedCopies", () => WindowsIntegration.OpenFolder(Path.Combine(root, "protection")))));
        var scope = Ui.Card(Ui.Stack(Ui.Label("DevelopmentScope", 20), Ui.Label("CurrentScope"), Ui.Label("LicenseNotice"),
            c.Action("OpenLicenseFolder", () => WindowsIntegration.OpenFolder(AppContext.BaseDirectory))));
        var content = Ui.Stack(Ui.Label("Settings", 28), general, data, scope);
        var scroll = Ui.Scroll(content);
        var left = Ui.Stack(Ui.Label("Settings", 23),
            c.Action("General", () => general.StartBringIntoView()),
            c.Action("DataBackups", () => data.StartBringIntoView()),
            c.Action("DevelopmentScope", () => scope.StartBringIntoView()));
        Content = Ui.Split(left, scroll);
    }
}
