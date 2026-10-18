using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Presentation;

namespace Rhi.App.Pages;

public sealed class SetupView : UserControl
{
    public SetupView(PageContext context)
    {
        var session = context.Session;
        var reconnect = session.Pointer is not null;
        var content = Ui.Stack(Ui.Label(reconnect ? "DataUnavailable" : "Welcome", 28),
            Ui.Label(session.InitializationBlocked ? "BootstrapInvalid" : reconnect ? "ReconnectDataHelp" : "DefaultDataUnavailable"),
            Ui.Label("BootstrapExplanation"),
            Ui.Text(session.Pointer?.Root ?? ""), Ui.Text(session.StartupError ?? ""));
        var actions = Ui.Row(context.Button(reconnect ? "ReconnectData" : "ChooseData", async () =>
        {
            var folder = await context.Windows.PickFolder();
            if (folder is not null)
            {
                session.Connect(folder, create: !reconnect);
                await Strings.UseAsync(session.State!.Preferences.Language);
                context.Refresh();
            }
        }, !session.InitializationBlocked));
        if (reconnect) Ui.AddToRow(actions, context.Button("Retry", async () =>
        {
            session.Connect(session.Pointer!.Root, false);
            await Strings.UseAsync(session.State!.Preferences.Language);
            context.Refresh();
        }));
        if (reconnect && !session.InitializationBlocked)
            Ui.AddToRow(actions, context.Button("StartNewData", async () =>
            {
                var target = Path.Combine(AppContext.BaseDirectory, "Data");
                var location = Ui.Text(target);
                var browse = context.Button("ChooseEmptyDataFolder", async () =>
                {
                    var selected = await context.Windows.PickFolder();
                    if (selected is not null) { target = selected; location.Text = target; }
                });
                var preview = Ui.Stack(Ui.Label("StartNewDataHelp"), location, browse);
                if (!await context.Confirm("StartNewData", preview, "CreateNewData",
                    configure: dialog => dialog.DefaultButton = ContentDialogButton.Primary)) return;
                session.StartNewWorkspace(target);
                await Strings.UseAsync(session.State!.Preferences.Language);
                context.Refresh();
            }));
        foreach (var action in actions.Children.OfType<Button>()) action.MinHeight = 44;
        content.Children.Add(actions);
        content.MaxWidth = 720; content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Center;
        Content = Ui.Scroll(new Border { Padding = new Thickness(28), Child = content });
    }
}
