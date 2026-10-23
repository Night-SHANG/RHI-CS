using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;
using Rhi.Core.Configuration;

namespace Rhi.App.Pages;

public sealed partial class SettingsView
{
    private FrameworkElement GeneralPage()
    {
        var state = c.Session.State!;
        Action? updateSave = null;
        var themes = new[] { "Light", "Dark", "System" };
        var languages = new[] { "System" }.Concat(Strings.AvailableLanguages).ToArray();
        var theme = Ui.Choice("Appearance", themes.Select(Strings.Get), Math.Max(0, Array.IndexOf(themes, c.ThemeDraft ?? state.Preferences.Theme)));
        var language = Ui.Choice("Language", languages.Select(Strings.LanguageName), Math.Max(0, Array.IndexOf(languages, c.LanguageDraft ?? state.Preferences.Language)));
        AutomationProperties.SetAutomationId(theme, "SettingsTheme");
        AutomationProperties.SetAutomationId(language, "SettingsLanguage");
        theme.IsEnabled = language.IsEnabled = !c.Session.Busy;
        theme.SelectionChanged += (_, _) => { c.ThemeDraft = themes[theme.SelectedIndex]; updateSave?.Invoke(); };
        language.SelectionChanged += (_, _) => { c.LanguageDraft = languages[language.SelectedIndex]; updateSave?.Invoke(); };
        var libraryRoot = Ui.Input("LibraryRoot", c.LibraryRootDraft ?? state.Preferences.LibraryRoot ?? "");
        AutomationProperties.SetAutomationId(libraryRoot, "SettingsLibraryRoot");
        libraryRoot.IsEnabled = !c.Session.Busy;
        libraryRoot.TextChanged += (_, _) => { c.LibraryRootDraft = libraryRoot.Text; updateSave?.Invoke(); };
        var browse = c.Button("BrowseFolder", async () =>
        {
            var path = await c.Windows.PickFolder(Directory.Exists(libraryRoot.Text) ? libraryRoot.Text : null);
            if (path is not null) libraryRoot.Text = path;
        }, !c.Session.Busy);
        AutomationProperties.SetAutomationId(browse, "SettingsLibraryRootBrowse");
        var open = c.Action("OpenFolder", () => WindowsIntegration.OpenFolder(libraryRoot.Text), !c.Session.Busy && Directory.Exists(libraryRoot.Text));
        libraryRoot.TextChanged += (_, _) => open.IsEnabled = !c.Session.Busy && Directory.Exists(libraryRoot.Text);
        AutomationProperties.SetAutomationId(open, "SettingsLibraryRootOpen");
        var save = c.Button("Save", async () =>
        {
            var root = libraryRoot.Text == c.Session.State!.Preferences.LibraryRoot
                ? c.Session.State.Preferences.LibraryRoot
                : string.IsNullOrWhiteSpace(libraryRoot.Text) ? null :
                    ConfigurationRules.LibraryDirectory(c.Session.State!, c.Session.Store!.Root, libraryRoot.Text);
            c.Session.Save(c.Session.State! with { Preferences = c.Session.State!.Preferences with
                { Theme = themes[theme.SelectedIndex], Language = languages[language.SelectedIndex], LibraryRoot = root } });
            libraryRoot.Text = root ?? "";
            c.ThemeDraft = c.LanguageDraft = null;
            c.LibraryRootDraft = null;
            await Strings.UseAsync(c.Session.State!.Preferences.Language);
            c.Refresh();
            updateSave?.Invoke();
        }, !c.Session.Busy);
        updateSave = () =>
        {
            var saved = c.Session.State!.Preferences;
            save.IsEnabled = !c.Session.Busy && (themes[theme.SelectedIndex] != saved.Theme ||
                languages[language.SelectedIndex] != saved.Language || libraryRoot.Text != (saved.LibraryRoot ?? ""));
        };
        updateSave();
        AutomationProperties.SetAutomationId(save, "SettingsSave");
        var actions = Ui.CompactActions(save);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var title = Ui.Label("General", 28);
        AutomationProperties.SetAutomationId(title, "SettingsGeneralTitle");
        var card = Ui.Card(Ui.Stack(Ui.Row(theme, language), Ui.DirectoryField(libraryRoot, browse, open), Ui.Label("LibraryRootHelp"), actions));
        AutomationProperties.SetAutomationId(card, "SettingsGeneralCard");
        return Ui.Stack(title, card);
    }

    private FrameworkElement OfflinePage()
    {
        var title = Ui.Label("MakeOfflineReShade", 28);
        AutomationProperties.SetAutomationId(title, "OfflineTitle");
        return Ui.Stack(title, Ui.Label("OfflinePageSummary"), OfflinePreparationPanel());
    }
}
