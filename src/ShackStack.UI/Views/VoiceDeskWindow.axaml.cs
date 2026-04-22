using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ShackStack.UI.ViewModels;

namespace ShackStack.UI.Views;

public partial class VoiceDeskWindow : Window
{
    private bool _spacePttActive;

    public VoiceDeskWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        KeyUp += OnWindowKeyUp;
        Deactivated += OnWindowDeactivated;
    }

    private async void OnPttPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not InputElement element
            || !e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(element);
        e.Handled = true;
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SetPttPressedAsync(true);
        }
    }

    private async void OnPttReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        e.Pointer.Capture(null);
        e.Handled = true;
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SetPttPressedAsync(false);
        }
    }

    private async void OnPttCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SetPttPressedAsync(false);
        }
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || _spacePttActive || !ShouldHandleSpacePtt())
        {
            return;
        }

        _spacePttActive = true;
        e.Handled = true;

        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SetPttPressedAsync(true);
        }
    }

    private async void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !_spacePttActive)
        {
            return;
        }

        _spacePttActive = false;
        e.Handled = true;

        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SetPttPressedAsync(false);
        }
    }

    private bool ShouldHandleSpacePtt()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is not TextBox
            && focused is not ComboBox
            && focused is not AutoCompleteBox
            && focused is not Slider;
    }

    private async void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _spacePttActive = false;

        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SetPttPressedAsync(false);
        }
    }
}
