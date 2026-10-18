using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;
using Rhi.Core.Preparation;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed partial class SettingsView
{
    private Border OfflinePreparationPanel()
    {
        var draft = c.Offline.Request;
        var installer = Ui.Input("OfflineInstaller", draft.Installer);
        var parent = Ui.Input("OfflineOutputParent", draft.Parent);
        var name = Ui.Input("OfflineFolderName", draft.FolderName);
        var architecture = Ui.Choice("OfflineArchitecture", ["x64", "x86"], draft.Architecture == "x86" ? 1 : 0);
        var apis = new[] { "DirectX 10/11/12", "DirectX 9", "OpenGL" };
        var api = Ui.Choice("GraphicsApi", apis, Math.Max(0, Array.IndexOf(apis, draft.Api)));
        AutomationProperties.SetAutomationId(installer, "OfflineInstallerInput");
        AutomationProperties.SetAutomationId(parent, "OfflineParentInput");
        AutomationProperties.SetAutomationId(name, "OfflineNameInput");
        AutomationProperties.SetAutomationId(architecture, "OfflineArchitectureChoice");
        AutomationProperties.SetAutomationId(api, "OfflineApiChoice");
        var file = c.Button("ChooseFile", async () => { var path = await c.Windows.PickFile(".exe"); if (path is not null) installer.Text = path; });
        var folder = c.Button("BrowseFolder", async () => { var path = await c.Windows.PickFolder(); if (path is not null) parent.Text = path; });
        AutomationProperties.SetAutomationId(file, "OfflineChooseInstaller");
        AutomationProperties.SetAutomationId(folder, "OfflineChooseFolder");
        var feedback = Ui.Text("");
        AutomationProperties.SetAutomationId(feedback, "OfflineValidation");
        OfflineReShadeRequest Read() => new(installer.Text, parent.Text, name.Text, architecture.SelectedIndex == 1 ? "x86" : "x64", apis[Math.Max(0, api.SelectedIndex)]);
        var create = c.Button("CreateOffline", async () =>
        {
            var request = Read();
            c.Offline = c.Offline with { Request = request, Failure = null };
            try
            {
                var output = await c.Session.CreateOfflineReShade(request);
                c.Offline = c.Offline with { Output = output, CompletedRequest = request };
            }
            catch (Exception ex) { c.Offline = c.Offline with { Failure = ex }; }
            c.Refresh();
        });
        create.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        AutomationProperties.SetAutomationId(create, "OfflineCreate");
        var reset = c.Action("ResetOffline", () => { c.Offline = OfflineDraft.Empty; c.Refresh(); }, !c.Session.Busy);
        AutomationProperties.SetAutomationId(reset, "OfflineReset");
        reset.MinHeight = create.MinHeight = 44;
        void Check(bool changed)
        {
            if (changed) c.Offline = c.Offline with { Request = Read(), Failure = null };
            feedback.Foreground = Ui.ThemeBrush("SecondaryTextBrush");
            create.IsEnabled = false;
            if (c.Session.Busy) { feedback.Text = Strings.Get("FormChecking"); return; }
            if (string.IsNullOrWhiteSpace(c.Offline.Request.Installer) || string.IsNullOrWhiteSpace(c.Offline.Request.Parent))
            { feedback.Text = Strings.Get("OfflineFillForm"); return; }
            try
            {
                OfflineReShade.Validate(c.Session.State!, c.Session.Store!.Root, c.Offline.Request);
                create.IsEnabled = true;
                feedback.Text = Strings.Get("OfflineReadyToCreate");
            }
            catch (DeploymentException ex) when (ex.Code == "OfflineOutputExists" && c.Offline.CompletedRequest == c.Offline.Request)
            { feedback.Text = Strings.Get("OfflineReady"); feedback.Foreground = Ui.ThemeBrush("SuccessBrush"); }
            catch (Exception ex) when (ex is DeploymentException or IOException or UnauthorizedAccessException or ArgumentException)
            { feedback.Text = ErrorText(ex); feedback.Foreground = Ui.ThemeBrush("ErrorBrush"); }
            if (c.Offline.Failure is { } failure)
            { feedback.Text = ErrorText(failure); feedback.Foreground = Ui.ThemeBrush("ErrorBrush"); }
        }
        foreach (var input in new Control[] { installer, parent, name, architecture, api, file, folder })
        {
            input.IsEnabled = !c.Session.Busy;
            if (input is TextBox text) text.TextChanged += (_, _) => Check(true);
            if (input is ComboBox choice) choice.SelectionChanged += (_, _) => Check(true);
        }
        var notice = new ScrollViewer { Content = feedback, Height = 72,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var title = Ui.Label("MakeOfflineReShade", 20);
        AutomationProperties.SetAutomationId(title, "OfflineTitle");
        var body = Ui.Stack(title, Ui.Label("OfflineHelp"),
            PickerRow(installer, file), Ui.Row(architecture, api), PickerRow(parent, folder), name,
            notice, Ui.Row(reset, create));
        if (c.Offline.Output is { } completed)
        {
            var output = Ui.Text(completed);
            AutomationProperties.SetAutomationId(output, "OfflineOutput");
            var open = c.Action("OpenFolder", () => WindowsIntegration.OpenFolder(completed), Directory.Exists(completed));
            AutomationProperties.SetAutomationId(open, "OfflineOpenOutput");
            body.Children.Add(Ui.Stack(Ui.Status("OfflineReady"), output, Ui.Label("OfflineNextStep"), open));
        }
        Check(false);
        var card = Ui.Card(body);
        AutomationProperties.SetAutomationId(card, "OfflinePreparationCard");
        return card;
    }

    private static string ErrorText(Exception ex) => ex is DeploymentException de
        ? Strings.Get(de.Code) : Strings.Get("InspectionFailed") + "\n" + ex.Message;

    private static Grid PickerRow(TextBox input, Button browse)
    {
        var row = Ui.Row(input, browse);
        row.ColumnDefinitions[1].Width = GridLength.Auto;
        browse.MaxWidth = 140;
        browse.VerticalAlignment = VerticalAlignment.Bottom;
        return row;
    }
}
