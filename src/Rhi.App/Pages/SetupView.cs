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
            Ui.Label("DataExplanation"), Ui.Label("BootstrapExplanation"),
            Ui.Text(session.Pointer?.Root ?? ""), Ui.Text(session.StartupError ?? ""));
        content.Children.Add(context.Button(reconnect ? "ReconnectData" : "ChooseData", async () =>
        {
            var folder = await context.Windows.PickFolder();
            if (folder is not null)
            {
                session.Connect(folder, create: !reconnect);
                await Strings.UseAsync(session.State!.Preferences.Language);
                context.Refresh();
            }
        }, !(session.StartupError is not null && session.Pointer is null)));
        if (reconnect) content.Children.Add(context.Button("Retry", async () =>
        {
            session.Connect(session.Pointer!.Root, false);
            await Strings.UseAsync(session.State!.Preferences.Language);
            context.Refresh();
        }));
        content.MaxWidth = 720; content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        Content = Ui.Scroll(new Border { Padding = new Thickness(28), Child = content });
    }
}
