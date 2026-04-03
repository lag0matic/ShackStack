using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ShackStack.UI.ViewModels;

namespace ShackStack.UI.Views;

public partial class MainWindow : Window
{
    private bool _spacePttActive;
    private SstvDeskWindow? _sstvDeskWindow;
    private WefaxDeskWindow? _wefaxDeskWindow;

    public MainWindow()
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
        if (DataContext is not MainWindowViewModel vm || vm.SelectedModePanelTabIndex != 0)
        {
            return false;
        }

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

    private void OnOpenSstvDeskClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_sstvDeskWindow is { IsVisible: true })
        {
            _sstvDeskWindow.Activate();
            return;
        }

        _sstvDeskWindow = new SstvDeskWindow
        {
            DataContext = DataContext,
        };
        _sstvDeskWindow.Closed += (_, _) => _sstvDeskWindow = null;
        _sstvDeskWindow.Show(this);
    }

    private void OnOpenWefaxDeskClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_wefaxDeskWindow is { IsVisible: true })
        {
            _wefaxDeskWindow.Activate();
            return;
        }

        _wefaxDeskWindow = new WefaxDeskWindow
        {
            DataContext = DataContext,
        };
        _wefaxDeskWindow.Closed += (_, _) => _wefaxDeskWindow = null;
        _wefaxDeskWindow.Show(this);
    }
}
