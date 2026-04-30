using Avalonia.Controls;
using Avalonia.Input;
using ShackStack.UI.ViewModels;

namespace ShackStack.UI.Views;

public partial class FreedvDeskWindow : Window
{
    private bool _freedvPttPointerDown;

    public FreedvDeskWindow()
    {
        InitializeComponent();
    }

    private async void OnFreedvPttPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not InputElement element
            || !e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(element);
        _freedvPttPointerDown = true;
        e.Handled = true;
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SetFreedvPttPressedAsync(true);
        }
    }

    private async void OnFreedvPttReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        e.Pointer.Capture(null);
        _freedvPttPointerDown = false;
        e.Handled = true;
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SetFreedvPttPressedAsync(false);
        }
    }

    private async void OnFreedvPttCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_freedvPttPointerDown)
        {
            return;
        }

        _freedvPttPointerDown = false;
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SetFreedvPttPressedAsync(false);
        }
    }
}
