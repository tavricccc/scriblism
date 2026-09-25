using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Scriblism.App;

public sealed class ResizeHandle : UserControl
{
    public ResizeHandle() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}
