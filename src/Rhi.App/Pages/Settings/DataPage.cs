using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage.Migration;

namespace Rhi.App.Pages;

public sealed partial class SettingsView
{
    private FrameworkElement DataPage()
    {
        var root = c.Session.Store!.Root;
        var path = Ui.Text(root);
        AutomationProperties.SetAutomationId(path, "SettingsDataPath");
        var migrate = c.Button(c.Session.PendingMigration is null ? "MigrateData" : "ContinueMigration", MigrateData);
        AutomationProperties.SetAutomationId(migrate, "SettingsMigrateData");
        var card = Ui.Stack(Ui.Label("DataContents"), path, Ui.Label("DataMigrationHelp"),
            Ui.HeaderActions("DataActions", c.FolderAction("OpenFolder", root, "SettingsOpenData"), migrate));
        if (c.Session.PendingMigration is { } pending)
        {
            card.Children.Add(Ui.Label("MigrationPending"));
            card.Children.Add(Ui.Text(Strings.Get("MigrationDestination") + ": " + pending.Destination));
        }
        return Ui.Stack(Ui.Label("ApplicationData", 28), Ui.Card(card));
    }

    private async Task MigrateData()
    {
        var root = c.Session.Store!.Root;
        var pending = c.Session.PendingMigration;
        string? destination = pending?.Destination;
        if (pending is null)
        {
            var parent = Ui.Input("MigrationParent", "");
            AutomationProperties.SetAutomationId(parent, "MigrationParentInput");
            var browse = c.Button("BrowseFolder", async () =>
            {
                var chosen = await c.Windows.PickFolder(Directory.Exists(parent.Text) ? parent.Text : Path.GetDirectoryName(root));
                if (chosen is not null) parent.Text = chosen;
            });
            AutomationProperties.SetAutomationId(browse, "MigrationBrowse");
            var preview = Ui.Text("");
            AutomationProperties.SetAutomationId(preview, "MigrationTargetPreview");
            parent.TextChanged += (_, _) =>
            {
                try { preview.Text = string.IsNullOrWhiteSpace(parent.Text) ? "" : Path.Combine(parent.Text, "Data"); }
                catch (ArgumentException) { preview.Text = ""; }
            };
            string Read()
            {
                var target = WorkspaceMigration.Destination(c.Session.Store!, parent.Text);
                if (PathPolicy.Within(AppContext.BaseDirectory, target)) throw new DeploymentException("OverlappingPaths");
                return target;
            }
            if (!await c.Edit("MigrateData", Ui.Stack(PickerRow(parent, browse), Ui.Label("MigrationDestination"), preview),
                () => { Read(); }, () => { destination = Read(); return Task.CompletedTask; }, [parent, browse], "Next")) return;
        }
        if (!await c.Confirm("MigrateData", Ui.Stack(Ui.Label("MigrationSource"), Ui.Text(pending?.Source ?? root),
            Ui.Label("MigrationDestination"), Ui.Text(destination!), Ui.Label("DataMigrationHelp")),
            pending is null ? "StartMigration" : "ContinueMigration")) return;
        await c.Session.MigrateData(destination);
        await c.Message("MigrateData", Strings.Get("MigrationComplete"));
    }
}
