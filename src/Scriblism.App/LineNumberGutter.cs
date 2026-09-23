using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Scriblism.Core;

namespace Scriblism.App;

/// <summary>Draw only visible logical line numbers, using RichEdit's own layout and scrolling.</summary>
internal sealed class LineNumberGutter : Canvas
{
    private readonly RichEditBox _editor;
    private readonly DocumentBuffer _buffer;
    private int _revision = -1;
    private int[] _starts = [0];
    private bool _queued;
    private ScrollViewer? _scroll;
    public LineNumberGutter(RichEditBox editor, DocumentBuffer buffer)
    {
        _editor = editor; _buffer = buffer; Width = 52; IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        SizeChanged += (_, _) => Refresh();
        _editor.Loaded += (_, _) =>
        {
            _scroll ??= DescendantScroll(_editor);
            if (_scroll is not null) { _scroll.ViewChanged -= OnScrolled; _scroll.ViewChanged += OnScrolled; }
            Refresh();
        };
        _editor.SizeChanged += (_, _) => Refresh();
    }
    private void OnScrolled(object? sender, ScrollViewerViewChangedEventArgs args) => Refresh();
    public void Refresh()
    {
        if (_queued || Visibility != Visibility.Visible) return;
        _queued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { _queued = false; Draw(); });
    }
    private void Draw()
    {
        Children.Clear();
        if (Visibility != Visibility.Visible || _editor.ActualHeight <= 0 || _buffer.Text.Length > SyntaxHighlighter.MaximumHighlightLength) return;
        if (_revision != _buffer.Revision)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < _buffer.Text.Length; i++) if (_buffer.Text[i] == '\n') starts.Add(i + 1);
            _starts = starts.ToArray(); _revision = _buffer.Revision;
        }
        Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, _editor.Padding.Top, Width, Math.Max(0, ActualHeight - _editor.Padding.Top - _editor.Padding.Bottom)) };
        try
        {
            // GetRect uses the RichEdit client coordinate space, in view pixels (DIPs).
            var low = 0; var high = _starts.Length;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                var rect = RectFor(mid);
                if (rect.Bottom < 0) low = mid + 1; else high = mid;
            }
            for (var line = low; line < _starts.Length && line < low + 200; line++)
            {
                var rect = RectFor(line); if (rect.Top > ActualHeight - _editor.Padding.Bottom) break;
                if (rect.Bottom < 0) continue;
                var label = new TextBlock
                {
                    Text = (line + 1).ToString(), FontFamily = _editor.FontFamily,
                    FontSize = _editor.FontSize, Width = Width - 12, TextAlignment = TextAlignment.Right,
                    Foreground = _editor.Foreground,
                    Opacity = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast ? 1 : .65
                };
                SetLeft(label, 0); SetTop(label, rect.Top);
                Children.Add(label);
            }
        }
        catch (System.Runtime.InteropServices.COMException) { /* Layout not available until the next arrange. */ }
    }
    private Windows.Foundation.Rect RectFor(int line)
    {
        var position = _starts[line];
        _editor.Document.GetRange(position, position).GetRect(PointOptions.Start | PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
        rect.Y += _editor.Padding.Top;
        return rect;
    }
    private static ScrollViewer? DescendantScroll(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroll) return scroll;
            if (DescendantScroll(child) is { } descendant) return descendant;
        }
        return null;
    }
}
