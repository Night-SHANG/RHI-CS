using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;

namespace Rhi.App.Pages;

public sealed partial class SettingsView
{
    private FrameworkElement GeneralPage()
    {
        var state = c.Session.State!;
        var themes = new[] { "Light", "Dark", "System" };
        var languages = new[] { "System" }.Concat(Strings.AvailableLanguages).ToArray();
        var theme = Ui.Choice("Appearance", themes.Select(Strings.Get), Math.Max(0, Array.IndexOf(themes, c.ThemeDraft ?? state.Preferences.Theme)));
        var language = Ui.Choice("Language", languages.Select(Strings.LanguageName), Math.Max(0, Array.IndexOf(languages, c.LanguageDraft ?? state.Preferences.Language)));
        AutomationProperties.SetAutomationId(theme, "SettingsTheme");
        AutomationProperties.SetAutomationId(language, "SettingsLanguage");
        theme.IsEnabled = language.IsEnabled = !c.Session.Busy;
        theme.SelectionChanged += (_, _) => c.ThemeDraft = themes[theme.SelectedIndex];
        language.SelectionChanged += (_, _) => c.LanguageDraft = languages[language.SelectedIndex];
        var save = c.Button("Save", async () =>
        {
            c.Session.Save(c.Session.State! with { Preferences = new(themes[theme.SelectedIndex], languages[language.SelectedIndex]) });
            c.ThemeDraft = c.LanguageDraft = null;
            await Strings.UseAsync(c.Session.State!.Preferences.Language);
            c.Refresh();
        }, !c.Session.Busy);
        AutomationProperties.SetAutomationId(save, "SettingsSave");
        var actions = Ui.CompactActions(save);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var title = Ui.Label("General", 28);
        AutomationProperties.SetAutomationId(title, "SettingsGeneralTitle");
        var card = Ui.Card(Ui.Stack(Ui.Row(theme, language), actions));
        AutomationProperties.SetAutomationId(card, "SettingsGeneralCard");
        return Ui.Stack(title, card);
    }

    private FrameworkElement DataPage()
    {
        var root = c.Session.Store!.Root;
        return Ui.Stack(Ui.Label("ApplicationData", 28), Ui.Card(Ui.Stack(Ui.Label("DataLocationHelp"),
            Ui.Text(root), Ui.Label("NoMigration"), c.FolderAction("OpenFolder", root, "SettingsOpenData"))));
    }

    private FrameworkElement OfflinePage()
    {
        var title = Ui.Label("MakeOfflineReShade", 28);
        AutomationProperties.SetAutomationId(title, "OfflineTitle");
        return Ui.Stack(title, Ui.Label("OfflinePageSummary"), OfflinePreparationPanel());
    }
}
