using Microsoft.UI.Xaml;

namespace Rhi.App;

public partial class App : Application
{
    private Window? window;
    private Mutex? singleInstance;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        singleInstance = new Mutex(true, @"Local\RHI-CS-WriteCoordinator", out var first);
        if (!first) { Exit(); return; }
        window = new MainWindow();
        window.Activate();
    }
}
