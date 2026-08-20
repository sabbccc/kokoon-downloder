using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Kokoon.UI.Controls;

public class HandCursorButton : Button
{
    public HandCursorButton()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}
