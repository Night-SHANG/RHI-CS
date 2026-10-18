using Microsoft.UI.Xaml;
using Rhi.App.Localization;
using Rhi.App.Services;

namespace Rhi.App;

public partial class App : Application
{
    private Window? window;
    private Mutex? singleInstance;
    public App() => InitializeComponent();
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        singleInstance = new Mutex(true, @"Local\RHI-CS-WriteCoordinator", out var first);
        if (!first) { Exit(); return; }
        var session = new AppSession();
        await Strings.InitializeAsync(session.State?.Preferences.Language ?? "System");
        window = new MainWindow(session);
        window.Activate();
    }
}
