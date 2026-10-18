using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Rhi.Core.Discovery;
using Windows.Storage.Pickers;

namespace Rhi.App.Services;

public sealed class WindowsIntegration(Window window)
{
    public async Task<string?> PickFolder()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public async Task<string?> PickFile(string extension = "*")
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static DiscoveryResult ScanSteam()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var path = key?.GetValue("SteamPath") as string;
        return path is not null ? SteamDiscovery.Scan(path) : new([], []);
    }

    public static void OpenFolder(string path)
    {
        Rhi.Core.Deployment.PathPolicy.CheckNoLinks(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static bool IsGameRunning(string exe)
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (System.ComponentModel.Win32Exception) { return true; }
                catch (InvalidOperationException) { }
            }
        }
        return false;
    }
}
