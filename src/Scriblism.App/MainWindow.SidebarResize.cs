using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Scriblism.App;

public sealed partial class MainWindow
{
    private bool _resizingSidebar;

    private void OnSidebarSplitterPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_settings.SidebarVisible || !e.GetCurrentPoint(WorkspaceGrid).Properties.IsLeftButtonPressed) return;
        _resizingSidebar = SidebarSplitter.CapturePointer(e.Pointer);
        e.Handled = _resizingSidebar;
    }

    private void OnSidebarSplitterMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingSidebar) return;
        var max = Math.Max(140, Math.Min(480, WorkspaceGrid.ActualWidth - 325));
        var width = Math.Clamp(e.GetCurrentPoint(WorkspaceGrid).Position.X, 140, max);
        SidebarColumn.Width = new GridLength(width);
        e.Handled = true;
    }

    private void OnSidebarSplitterReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingSidebar) return;
        _resizingSidebar = false;
        SidebarSplitter.ReleasePointerCapture(e.Pointer);
        _settings.SidebarWidth = SidebarColumn.Width.Value;
        _ = PersistSettings();
        e.Handled = true;
    }

    private void OnSidebarSplitterCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingSidebar) return;
        _resizingSidebar = false;
        _settings.SidebarWidth = SidebarColumn.Width.Value;
        _ = PersistSettings();
    }
}
