using Microsoft.UI.Xaml;
using Rhi.App.Localization;
using Rhi.App.Services;

namespace Rhi.App;

public partial class App : Application
{
    private Window? window;
    private Mutex? singleInstance;
    public App()
    {
        UnhandledException += (_, e) => Console.Error.WriteLine($"WinUI unhandled exception: {e.Message}\n{e.Exception}");
        Console.Error.WriteLine("Startup: application resources");
        InitializeComponent();
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Console.Error.WriteLine("Startup: session");
            singleInstance = new Mutex(true, @"Local\RHI-CS-WriteCoordinator", out var first);
            if (!first) { Exit(); return; }
            var session = new AppSession();
            Console.Error.WriteLine("Startup: localization");
            await Strings.InitializeAsync(session.State?.Preferences.Language ?? "System");
            Console.Error.WriteLine("Startup: main window");
            window = new MainWindow(session);
            window.Activate();
            Console.Error.WriteLine("Startup: activated");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            throw;
        }
    }
}
