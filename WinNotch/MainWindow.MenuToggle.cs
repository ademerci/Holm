using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinNotch;

public partial class MainWindow
{
    Button? _dismissedMenuButton;

    bool CloseButtonMenu(Button button, ContextMenu? menu)
    {
        // A popup dismisses itself on mouse-down, before the placement button raises Click on mouse-up.
        if (_dismissedMenuButton == button)
        {
            _dismissedMenuButton = null;
            return true;
        }
        if (menu?.IsOpen != true) return false;
        menu.IsOpen = false;
        _dismissedMenuButton = null;
        return true;
    }

    void RememberMenuButtonDismissal(ContextMenu? menu)
    {
        if (menu?.PlacementTarget is not Button button || Mouse.LeftButton != MouseButtonState.Pressed) return;
        var pointer = Mouse.GetPosition(button);
        if (!new Rect(new Size(button.ActualWidth, button.ActualHeight)).Contains(pointer)) return;
        _dismissedMenuButton = button;
        InputManager.Current.PostProcessInput -= ClearMenuButtonDismissal;
        InputManager.Current.PostProcessInput += ClearMenuButtonDismissal;
    }

    void ClearMenuButtonDismissal(object sender, ProcessInputEventArgs e)
    {
        // Wait until bubbling mouse-up has raised the button's Click; preview mouse-up is too early.
        if (e.StagingItem.Input is not MouseButtonEventArgs { ChangedButton: MouseButton.Left, ButtonState: MouseButtonState.Released } input
            || input.RoutedEvent != Mouse.MouseUpEvent) return;
        _dismissedMenuButton = null;
        InputManager.Current.PostProcessInput -= ClearMenuButtonDismissal;
    }
}
