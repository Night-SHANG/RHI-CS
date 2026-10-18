using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
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
        var languages = new[] { "System" }.Concat(Strings.AvailableLanguages).ToArray();
        var theme = Ui.Choice("Appearance", themes.Select(Strings.Get), Math.Max(0, Array.IndexOf(themes, state.Preferences.Theme)));
        AutomationProperties.SetAutomationId(theme, "SettingsTheme");
        var language = Ui.Choice("Language", languages.Select(Strings.LanguageName), Math.Max(0, Array.IndexOf(languages, state.Preferences.Language)));
        AutomationProperties.SetAutomationId(language, "SettingsLanguage");
        theme.IsEnabled = !c.Session.Busy; language.IsEnabled = !c.Session.Busy;
        var save = c.Button("Save", async () =>
        {
            c.Session.Save(state with { Preferences = new(themes[theme.SelectedIndex], languages[language.SelectedIndex]) });
            await Strings.UseAsync(c.Session.State!.Preferences.Language);
            c.Refresh();
        }, !c.Session.Busy);
        var root = c.Session.Store!.Root;
        var fields = Ui.Row(theme, language, save);
        save.VerticalAlignment = VerticalAlignment.Bottom;
        fields.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 560;
            fields.ColumnDefinitions.Clear(); fields.RowDefinitions.Clear();
            for (var i = 0; i < (narrow ? 1 : 3); i++) fields.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < (narrow ? 3 : 1); i++) fields.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var i = 0; i < fields.Children.Count; i++)
            { Grid.SetRow((FrameworkElement)fields.Children[i], narrow ? i : 0); Grid.SetColumn((FrameworkElement)fields.Children[i], narrow ? 0 : i); }
        };
        fields.RowSpacing = 12;
        var general = Ui.Card(Ui.Stack(Ui.Label("General", 20), fields));
        var data = Ui.Card(Ui.Stack(Ui.Label("ApplicationData", 20), Ui.Label("DataLocationHelp"), Ui.Text(root), Ui.Label("NoMigration"),
            c.Action("OpenFolder", () => WindowsIntegration.OpenFolder(root))));
        var protectionBody = Ui.Stack(Ui.Label("ProtectedCopies", 20), Ui.Text(Path.Combine(root, "protection")),
            Ui.Label("ProtectionRetention"),
            Ui.Row(c.Action("OpenProtectedCopies", () => WindowsIntegration.OpenFolder(Path.Combine(root, "protection"))),
                c.Button("RefreshProtection", c.Session.InspectProtection, !c.Session.Busy)));
        if (c.Session.ProtectionObservation is { } inventory)
        {
            var usage = Ui.Text(Strings.Format("ProtectionUsage", inventory.Operations.Length, inventory.Bytes));
            AutomationProperties.SetAutomationId(usage, "ProtectionUsage");
            protectionBody.Children.Add(usage);
            protectionBody.Children.Add(Ui.Text(Strings.Format("ProtectionMeasuredAt", inventory.CheckedAt.ToLocalTime())));
            foreach (var copy in inventory.Operations)
            {
                var game = copy.Entry.GameName ?? state.Games.FirstOrDefault(g => g.Id == copy.Entry.GameId)?.Name ?? copy.Entry.GameId.ToString();
                var library = copy.Entry.LibraryName ?? state.Libraries.FirstOrDefault(l => l.Id == copy.Entry.LibraryId)?.Name ?? copy.Entry.LibraryId.ToString();
                protectionBody.Children.Add(Ui.Stack(Ui.Text(game + " · " + library),
                    Ui.Text(copy.Entry.CreatedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)),
                    Ui.Text(copy.Entry.TargetRoot),
                    c.Action("OpenProtectedCopies", () => WindowsIntegration.OpenFolder(copy.Directory), Directory.Exists(copy.Directory))));
            }
        }
        else protectionBody.Children.Add(Ui.Label(c.Session.ProtectionReadAttempted && !c.Session.Busy ? "ProtectionReadFailed" : "ProtectionNotCounted"));
        var protection = Ui.Card(protectionBody);
        var license = new HyperlinkButton { Content = Strings.Get("OpenLicenseFolder"),
            HorizontalAlignment = HorizontalAlignment.Left };
        license.Click += async (_, _) => await c.Run(() =>
        { WindowsIntegration.OpenFolder(AppContext.BaseDirectory); return System.Threading.Tasks.Task.CompletedTask; });
        var content = Ui.Stack(Ui.Label("Settings", 28), general, data, protection, license);
        content.MaxWidth = 960;
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        var scroll = Ui.Scroll(content);
        AutomationProperties.SetAutomationId(scroll, "SettingsScroll");
        Content = new Border { Padding = new Thickness(24), Child = scroll };
        Loaded += async (_, _) =>
        {
            if (!c.Session.Busy && !c.Session.ProtectionReadAttempted)
                await c.Run(c.Session.InspectProtection);
        };
    }
}
