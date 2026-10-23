using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    private static Action ClipFormViewport(ScrollViewer scroll, Grid container)
    {
        // WinUI's UIA peer can include descendants beyond the native
        // ScrollContentPresenter clip. Expose the actual arranged viewport,
        // not the bounds of partially scrolled-out fields, to accessibility.
        var clip = new RectangleGeometry();
        scroll.Clip = clip;
        var active = true;
        var callbacks = 0;
        var ci = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        void Lifecycle(string reason) {
            if (ci) Console.Error.WriteLine($"Viewport lifecycle: {reason}; loaded={scroll.IsLoaded}; active={active}; callbacks={callbacks}");
        }
        string? lastDiagnostic = null;
        var diagnostics = 0;
        void UpdateClip(object? sender, object args)
        {
            callbacks++;
            if (!active || !scroll.IsLoaded) return;
            // RenderSize can exceed the allocated slot after layout squeezing.
            // Slot changes do not necessarily raise SizeChanged.
            var slot = LayoutInformation.GetLayoutSlot(scroll);
            var bounds = new Rect(0, 0, Math.Max(0, Math.Min(scroll.ActualWidth, slot.Width)),
                Math.Max(0, Math.Min(scroll.ActualHeight, slot.Height)));
            if (clip.Rect != bounds) clip.Rect = bounds;
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && diagnostics < 300)
            {
                var position = scroll.TransformToVisual(container).TransformPoint(new Point());
                var diagnostic = $"Viewport layout: slot={slot}; actual={scroll.ActualWidth},{scroll.ActualHeight}; " +
                    $"desired={scroll.DesiredSize}; row={container.RowDefinitions[0].ActualHeight}; " +
                    $"container={container.ActualHeight}; offset={position}; clip={bounds}";
                if (diagnostic != lastDiagnostic)
                {
                    Console.Error.WriteLine(diagnostic);
                    lastDiagnostic = diagnostic;
                    diagnostics++;
                }
            }
        }
        // Keep the handler strongly reachable and also observe size notifications.
        // LayoutUpdated alone did not refresh this viewport after wrapped text and
        // feedback changed a live ContentDialog in the Windows regression fixture.
        EventHandler<object> layoutChanged = UpdateClip;
        void SizeChanged(object sender, SizeChangedEventArgs args)
        {
            UpdateClip(sender, args);
            scroll.DispatcherQueue.TryEnqueue(() => UpdateClip(null, EventArgs.Empty));
        }
        scroll.Loaded += (_, _) =>
        {
            Lifecycle("loaded");
            if (!active) return;
            scroll.LayoutUpdated += layoutChanged;
            scroll.SizeChanged += SizeChanged;
            container.SizeChanged += SizeChanged;
            UpdateClip(null, EventArgs.Empty);
        };
        scroll.Unloaded += (_, _) =>
        {
            Lifecycle("unloaded");
            scroll.LayoutUpdated -= layoutChanged;
            scroll.SizeChanged -= SizeChanged;
            container.SizeChanged -= SizeChanged;
        };
        // Read-only CI probe: it never repairs clipping or forces a layout pass.
        // This distinguishes an inactive subscription from stale object dimensions.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        string? lastProbe = null;
        var probes = 0;
        timer.Tick += (_, _) =>
        {
            if (!active || ++probes > 240) { timer.Stop(); return; }
            try
            {
                var line = $"Viewport probe: loaded={scroll.IsLoaded}; root={scroll.XamlRoot is not null}; " +
                    $"actual={scroll.ActualWidth},{scroll.ActualHeight}; slot={LayoutInformation.GetLayoutSlot(scroll)}; " +
                    $"row={container.RowDefinitions[0].ActualHeight}; container={container.ActualHeight}; " +
                    $"clip={clip.Rect}; sameClip={ReferenceEquals(scroll.Clip, clip)}; callbacks={callbacks}";
                if (line != lastProbe) { Console.Error.WriteLine(line); lastProbe = line; }
            }
            catch (Exception ex) { Console.Error.WriteLine($"Viewport probe unavailable: {ex.GetType().Name}"); }
        };
        if (ci) timer.Start();
        return () =>
        {
            active = false;
            timer.Stop();
            Lifecycle("disposed");
            scroll.LayoutUpdated -= layoutChanged;
            scroll.SizeChanged -= SizeChanged;
            container.SizeChanged -= SizeChanged;
        };
    }
}
