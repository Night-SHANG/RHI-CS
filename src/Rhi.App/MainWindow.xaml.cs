using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Pages;
using Rhi.App.Presentation;
using Rhi.App.Services;

namespace Rhi.App;

public sealed partial class MainWindow : Window
{
    private readonly AppSession session;
    private readonly PageContext context;
    private string page = "Games";
    private bool closingDialog;
    private bool refreshing;

    public MainWindow(AppSession session)
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1320, 860));
        this.session = session;
        context = new(session, new WindowsIntegration(this), Root, Refresh);
        session.Changed += Refresh;
        session.Progress += p =>
        {
            TaskText.Text = Strings.Format("TaskProgress", Strings.Get(p.Phase), p.Library, p.Completed, p.Total);
            if (p.Phase == "Recovering") StopButton.IsEnabled = false;
        };
        AppWindow.Closing += async (_, e) =>
        {
            if (!session.IsWriting) return;
            e.Cancel = true;
            if (closingDialog) return;
            closingDialog = true;
            try
            {
                if (await context.Confirm("TaskRunning", Ui.Label("CloseDuringTask"), "StopAndRestore")) session.Stop();
            }
            finally { closingDialog = false; }
        };
        Closed += (_, _) => session.Dispose();
        Root.ActualThemeChanged += (_, _) => { if (!refreshing) Refresh(); };
        Refresh();
    }

    private void Refresh()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
        session.InspectConnection();
        var preferences = session.State?.Preferences ?? new Rhi.Core.Models.Preferences();
        Root.RequestedTheme = preferences.Theme switch { "Dark" => ElementTheme.Dark, "System" => ElementTheme.Default, _ => ElementTheme.Light };
        Ui.CurrentTheme = Root.ActualTheme;
        GamesNav.Content = Strings.Get("Games"); LibrariesNav.Content = Strings.Get("Libraries"); SettingsNav.Content = Strings.Get("Settings");
        GamesNav.IsChecked = page == "Games"; LibrariesNav.IsChecked = page == "Libraries"; SettingsNav.IsChecked = page == "Settings";
        StopButton.Content = Strings.Get(session.StopRequested ? "Stopping" : "StopAndRestore");
        StopButton.Visibility = session.IsWriting ? Visibility.Visible : Visibility.Collapsed;
         StopButton.IsEnabled = !session.StopRequested && !session.Recovering;
        TaskProgressBar.Visibility = session.Busy ? Visibility.Visible : Visibility.Collapsed;
        ResultButton.Content = Strings.Get("TaskResults"); ResultButton.IsEnabled = session.Results.Count > 0 && !session.Busy;
         TaskText.Text = session.Busy && session.CurrentProgress is { } task
             ? Strings.Format("TaskProgress", Strings.Get(task.Phase), task.Library, task.Completed, task.Total)
             : Strings.Get(session.Busy ? "Busy" : session.Results.Count > 0 ? "TaskEnded" : "Idle");
        PageHost.Content = session.Store is null ? new SetupView(context) : page switch
        {
            "Libraries" => new LibrariesView(context),
            "Settings" => new SettingsView(context),
            _ => new GamesView(context)
        };
        }
        finally { refreshing = false; }
    }
    private void Games_Click(object sender, RoutedEventArgs e) { page = "Games"; Refresh(); }
    private void Libraries_Click(object sender, RoutedEventArgs e) { page = "Libraries"; Refresh(); }
    private void Settings_Click(object sender, RoutedEventArgs e) { page = "Settings"; Refresh(); }
    private void Stop_Click(object sender, RoutedEventArgs e)
    { session.Stop(); StopButton.IsEnabled = false; StopButton.Content = Strings.Get("Stopping"); }
    private async void Result_Click(object sender, RoutedEventArgs e) => await context.Run(() => context.Message("TaskResults",
        string.Join("\n\n", session.Results.Select(r =>
            (session.State?.Libraries.FirstOrDefault(l => l.Id == r.LibraryId)?.Name ?? r.LibraryId.ToString()) +
            " · " + Strings.Get(r.Status.ToString()) + "\n" + Strings.Get(r.Detail) + "\n" + r.OperationId))));
}
