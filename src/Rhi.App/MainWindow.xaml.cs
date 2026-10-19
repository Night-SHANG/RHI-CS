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
    private string? presentedPage;
    private object? presentedStore;
    private string? presentedStyle;

    public MainWindow(AppSession session)
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        Root.Loaded += (_, _) =>
        {
            var scale = Root.XamlRoot.RasterizationScale;
            var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,
                Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            void UpdateMinimumSize()
            {
                if (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter) return;
                var currentScale = Root.XamlRoot.RasterizationScale;
                var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
                presenter.PreferredMinimumWidth = Math.Min((int)Math.Round(760 * currentScale), area.Width);
                presenter.PreferredMinimumHeight = Math.Min((int)Math.Round(540 * currentScale), area.Height);
            }
            UpdateMinimumSize();
            Root.XamlRoot.Changed += (_, _) => UpdateMinimumSize();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(
                Math.Min((int)Math.Round(1320 * scale), work.Width),
                Math.Min((int)Math.Round(860 * scale), work.Height)));
        };
        this.session = session;
        context = new(session, new WindowsIntegration(this), Root, Refresh);
        session.Changed += Refresh;
        session.Progress += p =>
        {
            TaskText.Text = ProgressText(p);
            if (p.Phase == "Recovering") StopButton.IsEnabled = false;
        };
        AppWindow.Closing += async (_, e) =>
        {
            if (session.SavingConfiguration) { e.Cancel = true; return; }
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
        if (session.PendingMigration is not null && session.Store is not null) { page = "Settings"; context.SettingsSection = "Data"; }
        var preferences = session.State?.Preferences ?? new Rhi.Core.Models.Preferences();
        Root.RequestedTheme = preferences.Theme switch { "Dark" => ElementTheme.Dark, "System" => ElementTheme.Default, _ => ElementTheme.Light };
        Ui.CurrentTheme = Root.ActualTheme;
        GamesNav.Content = Strings.Get("Games"); LibrariesNav.Content = Strings.Get("Libraries"); SettingsNav.Content = Strings.Get("Settings");
        GamesNav.IsChecked = page == "Games"; LibrariesNav.IsChecked = page == "Libraries"; SettingsNav.IsChecked = page == "Settings";
        GamesIndicator.Visibility = page == "Games" ? Visibility.Visible : Visibility.Collapsed;
        LibrariesIndicator.Visibility = page == "Libraries" ? Visibility.Visible : Visibility.Collapsed;
        SettingsIndicator.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Content = Strings.Get(session.StopRequested ? "Stopping" : "StopAndRestore");
        StopButton.Visibility = session.IsWriting ? Visibility.Visible : Visibility.Collapsed;
         StopButton.IsEnabled = !session.StopRequested && !session.Recovering;
        TaskProgressBar.Visibility = session.Busy ? Visibility.Visible : Visibility.Collapsed;
        ResultButton.Content = Strings.Get("TaskResults"); ResultButton.IsEnabled = session.Results.Count > 0 && !session.Busy;
         TaskText.Text = session.MigrationPhase is { } phase ? Strings.Get(phase) : session.IsWriting && session.CurrentProgress is { } task
             ? ProgressText(task)
             : session.Results.Count > 0 && !session.Busy ? TaskHeading() + " · " + Strings.Get("TaskEnded") : Strings.Get(session.Busy ? "Busy" : "Idle");
        var style = Root.ActualTheme + ":" + Strings.Get("Games") + ":" + preferences.Language;
        // Busy notifications update the shell, never remove the user's current view.
        GamesNav.IsEnabled = LibrariesNav.IsEnabled = SettingsNav.IsEnabled = !session.Busy;
        if (session.PendingMigration is not null) GamesNav.IsEnabled = LibrariesNav.IsEnabled = false;
        PageHost.IsEnabled = !session.Busy;
        if (!session.Busy)
        {
            if (PageHost.Content is null || presentedPage != page || presentedStore != session.Store || presentedStyle != style)
            {
                PageHost.Content = session.Store is null ? new SetupView(context) : page switch
                {
                    "Libraries" => new LibrariesView(context),
                    "Settings" => new SettingsView(context),
                    _ => new GamesView(context)
                };
                presentedPage = page; presentedStore = session.Store; presentedStyle = style;
            }
            else if (PageHost.Content is GamesView games) games.RefreshData();
            else if (PageHost.Content is LibrariesView libraries) libraries.RefreshData();
        }
        }
        finally { refreshing = false; }
    }
    private void Games_Click(object sender, RoutedEventArgs e) { page = "Games"; Refresh(); }
    private void Libraries_Click(object sender, RoutedEventArgs e) { page = "Libraries"; Refresh(); }
    private void Settings_Click(object sender, RoutedEventArgs e) { page = "Settings"; Refresh(); }
    private void Stop_Click(object sender, RoutedEventArgs e)
    { session.Stop(); StopButton.IsEnabled = false; StopButton.Content = Strings.Get("Stopping"); }
}
