using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    // Read-only CI evidence, not layout or accessibility behavior. UIA's bounds
    // can include descendants outside ScrollContentPresenter's native viewport.
    private static Action ObserveFormViewport(ScrollViewer scroll, Grid container)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RHI_UI_GEOMETRY") != "1") return static () => { };
        var id = Guid.NewGuid().ToString("N");
        var active = true;
        var sequence = 0;
        var overflow = false;
        string? previous = null;
        void Emit(string kind, object? geometry = null) => Console.Error.WriteLine("RHI_UI_GEOMETRY:" +
            JsonSerializer.Serialize(new { kind, id, sequence = ++sequence, geometry }));
        object Bounds(FrameworkElement element)
        {
            var position = element.TransformToVisual(null).TransformPoint(new Point());
            return new { x = position.X, y = position.Y, width = element.ActualWidth, height = element.ActualHeight };
        }
        void Observe(object? sender, object args)
        {
            if (!active || overflow || scroll.XamlRoot is null) return;
            if (sequence >= 2000) { overflow = true; Emit("overflow"); return; }
            try
            {
                // The first breadth-first presenter is this viewer's template,
                // before nested TextBox viewers. Verify viewport dimensions too.
                FrameworkElement? presenter = null;
                var queue = new Queue<DependencyObject>();
                queue.Enqueue(scroll);
                while (queue.Count > 0 && presenter is null)
                {
                    var node = queue.Dequeue();
                    if (node is FrameworkElement element && element.GetType().Name == "ScrollContentPresenter") presenter = element;
                    else for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++) queue.Enqueue(VisualTreeHelper.GetChild(node, index));
                }
                if (presenter is null || presenter.ActualHeight <= 0)
                {
                    previous = null;
                    Emit("unavailable", new { reason = "PresenterNotReady" });
                    return;
                }
                if (Math.Abs(presenter.ActualHeight - scroll.ViewportHeight) > 1 ||
                    Math.Abs(presenter.ActualWidth - scroll.ViewportWidth) > 1)
                    throw new InvalidOperationException("Unexpected presenter dimensions.");
                var feedback = (FrameworkElement)container.Children[1];
                var geometry = new { scale = scroll.XamlRoot.RasterizationScale, viewport = Bounds(presenter),
                    feedback = Bounds(feedback), feedbackVisible = feedback.Visibility == Visibility.Visible };
                var encoded = JsonSerializer.Serialize(geometry);
                if (encoded == previous) return;
                previous = encoded;
                Emit("snapshot", geometry);
            }
            catch (Exception ex) { previous = null; Emit("unavailable", new { reason = ex.GetType().Name }); }
        }
        EventHandler<object> layoutChanged = Observe;
        void SizeChanged(object sender, SizeChangedEventArgs args) => Observe(sender, args);
        void Loaded(object sender, RoutedEventArgs args) => Observe(sender, args);
        Emit("open");
        // ContentDialog can emit Unloaded while reparenting a still-live popup.
        // Observe only during ShowAsync, never change clip, measure or arrange.
        scroll.LayoutUpdated += layoutChanged;
        scroll.SizeChanged += SizeChanged;
        container.SizeChanged += SizeChanged;
        scroll.Loaded += Loaded;
        return () =>
        {
            if (!active) return;
            active = false;
            scroll.LayoutUpdated -= layoutChanged;
            scroll.SizeChanged -= SizeChanged;
            container.SizeChanged -= SizeChanged;
            scroll.Loaded -= Loaded;
            Emit("closed");
        };
    }
}
