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
        // UIA can include descendants outside the native presenter clip.
        // Expose the visible arranged viewport without changing the form's layout.
        var clip = new RectangleGeometry();
        scroll.Clip = clip;
        var active = true;
        string? previous = null;
        var diagnostics = 0;
        void UpdateClip(object? sender, object args)
        {
            if (!active || scroll.XamlRoot is null) return;
            var slot = LayoutInformation.GetLayoutSlot(scroll);
            var bounds = new Rect(0, 0, Math.Max(0, Math.Min(scroll.ActualWidth, slot.Width)),
                Math.Max(0, Math.Min(scroll.ActualHeight, slot.Height)));
            if (clip.Rect != bounds) clip.Rect = bounds;
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
                scroll.XamlRoot.RasterizationScale >= 1.7 && diagnostics < 60)
            {
                try
                {
                var offset = scroll.TransformToVisual(container).TransformPoint(new Point());
                var line = $"Viewport native: scale={scroll.XamlRoot.RasterizationScale}; slot={slot}; actual={scroll.ActualWidth},{scroll.ActualHeight}; desired={scroll.DesiredSize}; row={container.RowDefinitions[0].ActualHeight}; container={container.ActualHeight}; offset={offset}; viewport={scroll.ViewportWidth},{scroll.ViewportHeight}; extent={scroll.ExtentWidth},{scroll.ExtentHeight}; clip={clip.Rect}; sameClip={ReferenceEquals(scroll.Clip, clip)}";
                if (line != previous)
                {
                    previous = line; diagnostics++;
                    Console.Error.WriteLine(line);
                    var feedback = (FrameworkElement)container.Children[1];
                    Console.Error.WriteLine($"Viewport feedback: actual={feedback.ActualWidth},{feedback.ActualHeight}; slot={LayoutInformation.GetLayoutSlot(feedback)}; rootPosition={feedback.TransformToVisual(null).TransformPoint(new Point())}; scrollRoot={scroll.TransformToVisual(null).TransformPoint(new Point())}");
                    var descendants = new Queue<DependencyObject>();
                    descendants.Enqueue(scroll);
                    while (descendants.Count > 0)
                    {
                        var child = descendants.Dequeue();
                        if (child is FrameworkElement presenter && presenter.GetType().Name == "ScrollContentPresenter")
                            Console.Error.WriteLine($"Viewport presenter: actual={presenter.ActualWidth},{presenter.ActualHeight}; slot={LayoutInformation.GetLayoutSlot(presenter)}; rootPosition={presenter.TransformToVisual(null).TransformPoint(new Point())}");
                        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(child); index++) descendants.Enqueue(VisualTreeHelper.GetChild(child, index));
                    }
                    DependencyObject? node = scroll;
                    for (var depth = 0; node is not null && depth < 12; depth++, node = VisualTreeHelper.GetParent(node))
                        if (node is FrameworkElement element)
                            Console.Error.WriteLine($"Viewport ancestor: depth={depth}; type={element.GetType().Name}; name={element.Name}; actual={element.ActualWidth},{element.ActualHeight}; slot={LayoutInformation.GetLayoutSlot(element)}; rootPosition={element.TransformToVisual(null).TransformPoint(new Point())}; clip={(element.Clip as RectangleGeometry)?.Rect}");
                }
                }
                catch (Exception ex) { Console.Error.WriteLine($"Viewport diagnostic unavailable: {ex.GetType().Name}"); }
            }
        }
        EventHandler<object> layoutChanged = UpdateClip;
        void SizeChanged(object sender, SizeChangedEventArgs args)
        {
            UpdateClip(sender, args);
            scroll.DispatcherQueue.TryEnqueue(() => UpdateClip(null, EventArgs.Empty));
        }
        void Loaded(object sender, RoutedEventArgs args) => UpdateClip(sender, args);
        // ContentDialog reparenting can emit Unloaded while IsLoaded is still true.
        // Own these subscriptions for ShowAsync's lifetime, not tree attachment.
        scroll.LayoutUpdated += layoutChanged;
        scroll.SizeChanged += SizeChanged;
        container.SizeChanged += SizeChanged;
        scroll.Loaded += Loaded;
        return () =>
        {
            if (!active) return;
            active = false; // Already queued callbacks must not touch a closed form.
            scroll.LayoutUpdated -= layoutChanged;
            scroll.SizeChanged -= SizeChanged;
            container.SizeChanged -= SizeChanged;
            scroll.Loaded -= Loaded;
        };
    }
}
