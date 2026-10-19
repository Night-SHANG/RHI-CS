using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;

namespace Rhi.App.Pages;

public sealed partial class SettingsView
{
    private FrameworkElement ProtectionPage()
    {
        var root = Path.Combine(c.Session.Store!.Root, "protection");
        var records = new StackPanel();
        var scroll = Ui.Scroll(records);
        scroll.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        scroll.SizeChanged += (_, e) => records.Width = e.NewSize.Width;
        AutomationProperties.SetAutomationId(scroll, "ProtectionScroll");
        AutomationProperties.SetAutomationId(records, "ProtectionInventory");
        scroll.ViewChanged += (_, _) => c.SettingsOffsets["Protection"] = scroll.VerticalOffset;
        scroll.Loaded += (_, _) => scroll.ChangeView(null, c.SettingsOffsets.GetValueOrDefault("Protection"), null, true);
        var usage = Ui.Text("");
        AutomationProperties.SetAutomationId(usage, "ProtectionUsage");
        var measured = Ui.Secondary("");
        measured.HorizontalAlignment = HorizontalAlignment.Right;
        var refresh = c.Button("RefreshProtection", () => Task.CompletedTask, !c.Session.Busy);
        AutomationProperties.SetAutomationId(refresh, "RefreshProtection");
        void RenderInventory()
        {
            var offset = scroll.VerticalOffset;
            records.Children.Clear();
            var inventory = c.Session.ProtectionObservation;
            usage.Text = inventory is null ? "" : Strings.Format("ProtectionSummary", inventory.Operations.Length, inventory.Bytes);
            ToolTipService.SetToolTip(usage, inventory is null ? "" : Strings.Format("ProtectionUsage", inventory.Operations.Length, inventory.Bytes));
            measured.Text = inventory is null ? "" : Strings.Format("ProtectionMeasuredAt", inventory.CheckedAt.ToLocalTime());
            if (inventory is null || inventory.Operations.Length == 0)
            {
                records.Children.Add(new Border { Padding = new Thickness(24), Child = Ui.Label(inventory is not null ? "ProtectionEmpty" :
                    c.Session.ProtectionReadAttempted ? "ProtectionReadFailed" : "ProtectionNotCounted") });
                return;
            }
            foreach (var copy in inventory.Operations)
            {
                var state = c.Session.State!;
                var game = copy.Entry.GameName ?? state.Games.FirstOrDefault(g => g.Id == copy.Entry.GameId)?.Name ?? copy.Entry.GameId.ToString();
                var library = copy.Entry.LibraryName ?? state.Libraries.FirstOrDefault(l => l.Id == copy.Entry.LibraryId)?.Name ?? copy.Entry.LibraryId.ToString();
                var gameName = Ui.OneLine(game);
                gameName.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                var owner = Ui.Stack(gameName, Ui.OneLine(library, true));
                var version = Ui.Stack(Ui.OneLine(copy.Entry.VersionLabel ?? Strings.Get("LegacyProtection")), Ui.OneLine(copy.Entry.TargetRoot, true));
                var date = copy.Entry.CreatedAt.ToLocalTime();
                var time = Ui.Stack(Ui.OneLine(date.ToString("d")), Ui.OneLine(date.ToString("t"), true));
                owner.Spacing = version.Spacing = time.Spacing = 6;
                var open = c.FolderAction("OpenProtectionItem", copy.Directory, "OpenProtection_" + copy.Entry.OperationId.ToString("N"));
                open.Width = 128;
                records.Children.Add(Ui.TableLine(ProtectionRow(owner, version, time, open)));
            }
            scroll.DispatcherQueue.TryEnqueue(() => scroll.ChangeView(null, offset, null, true));
        }
        async Task RefreshInventory()
        {
            refresh.IsEnabled = false;
            measured.Text = Strings.Get("FormChecking");
            try { await c.Session.InspectProtection(); }
            finally { RenderInventory(); refresh.IsEnabled = !c.Session.Busy; }
        }
        refresh.Click += async (_, _) => await c.Run(RefreshInventory);
        var actions = Ui.Actions(148, c.FolderAction("OpenProtectionRoot", root, "OpenProtectionRoot"), refresh);
        actions.ColumnDefinitions[1].Width = new GridLength(108);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var heading = Ui.Row(Ui.Label("ProtectedCopies", 28), actions);
        heading.ColumnDefinitions[1].Width = GridLength.Auto;
        var head = Ui.Stack(heading, Ui.Label("ProtectionPageSummary"), Ui.Row(usage, measured));
        head.Spacing = 16;
        var table = new Grid();
        table.RowDefinitions.Add(new() { Height = GridLength.Auto });
        table.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        table.Children.Add(Ui.TableLine(ProtectionRow(Ui.Label("ProtectionOwner"), Ui.Label("ProtectionVersionTarget"), Ui.Label("ProtectionTime"), Ui.Label("Actions"), true), true));
        Grid.SetRow(scroll, 1); table.Children.Add(scroll);
        var page = new Grid { RowSpacing = 20 };
        page.RowDefinitions.Add(new() { Height = GridLength.Auto });
        page.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new() { Height = GridLength.Auto });
        page.Children.Add(head); Grid.SetRow(table, 1); page.Children.Add(table);
        var retention = Ui.Label("ProtectionRetention");
        Grid.SetRow(retention, 2); page.Children.Add(retention);
        RenderInventory();
        page.Loaded += async (_, _) =>
        {
            if (!c.Session.Busy && !c.Session.ProtectionReadAttempted) await c.Run(RefreshInventory);
        };
        return page;
    }

    private static Grid ProtectionRow(FrameworkElement owner, FrameworkElement version, FrameworkElement time, FrameworkElement action, bool header = false)
    {
        var row = Ui.Columns([1.1, 2.5, 0.8, 1], owner, version, time, action);
        row.MinHeight = 0;
        row.Padding = new Thickness(20, 12, 20, 12);
        row.Height = header ? 48 : 92;
        row.ColumnDefinitions[3].Width = new GridLength(128);
        foreach (FrameworkElement cell in row.Children) cell.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }
}
