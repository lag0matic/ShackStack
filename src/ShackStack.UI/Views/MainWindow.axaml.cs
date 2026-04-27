using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ShackStack.UI.ViewModels;

namespace ShackStack.UI.Views;

public partial class MainWindow : Window
{
    private bool _spacePttActive;
    private bool _isClosing;
    private SstvDeskWindow? _sstvDeskWindow;
    private VoiceDeskWindow? _voiceDeskWindow;
    private CwDeskWindow? _cwDeskWindow;
    private RttyDeskWindow? _rttyDeskWindow;
    private WefaxDeskWindow? _wefaxDeskWindow;
    private WsjtxDeskWindow? _wsjtxDeskWindow;
    private Js8DeskWindow? _js8DeskWindow;
    private LongwaveDeskWindow? _longwaveDeskWindow;

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

    private void OnOpenVoiceDeskClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_voiceDeskWindow is { IsVisible: true })
        {
            _voiceDeskWindow.Activate();
            return;
        }

        _voiceDeskWindow = new VoiceDeskWindow
        {
            DataContext = DataContext,
        };
        _voiceDeskWindow.Closed += (_, _) => _voiceDeskWindow = null;
        _voiceDeskWindow.Show(this);
    }

    private void OnOpenCwDeskClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_cwDeskWindow is { IsVisible: true })
        {
            _cwDeskWindow.Activate();
            return;
        }

        _cwDeskWindow = new CwDeskWindow
        {
            DataContext = DataContext,
        };
        _cwDeskWindow.Closed += (_, _) => _cwDeskWindow = null;
        _cwDeskWindow.Show(this);
    }

    private void OnOpenRttyDeskClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_rttyDeskWindow is { IsVisible: true })
        {
            _rttyDeskWindow.Activate();
            return;
        }

        _rttyDeskWindow = new RttyDeskWindow
        {
            DataContext = DataContext,
        };
        _rttyDeskWindow.Closed += (_, _) => _rttyDeskWindow = null;
        _rttyDeskWindow.Show(this);
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

    private void OnOpenWsjtxDeskClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_wsjtxDeskWindow is { IsVisible: true })
        {
            _wsjtxDeskWindow.Activate();
            return;
        }

        _wsjtxDeskWindow = new WsjtxDeskWindow
        {
            DataContext = DataContext,
        };
        _wsjtxDeskWindow.Closed += (_, _) => _wsjtxDeskWindow = null;
        _wsjtxDeskWindow.Show(this);
    }

    private void OnOpenJs8DeskClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ActivateJs8Desk();
        }

        if (_js8DeskWindow is { IsVisible: true })
        {
            _js8DeskWindow.Activate();
            return;
        }

        _js8DeskWindow = new Js8DeskWindow
        {
            DataContext = DataContext,
        };
        _js8DeskWindow.Closed += (_, _) => _js8DeskWindow = null;
        _js8DeskWindow.Show(this);
    }

    private void OnOpenLongwaveDeskClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_longwaveDeskWindow is { IsVisible: true })
        {
            _longwaveDeskWindow.Activate();
            return;
        }

        _longwaveDeskWindow = new LongwaveDeskWindow
        {
            DataContext = DataContext,
        };
        _longwaveDeskWindow.Closed += (_, _) => _longwaveDeskWindow = null;
        _longwaveDeskWindow.Show(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _isClosing = true;
        CloseDeskWindows();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        KeyDown -= OnWindowKeyDown;
        KeyUp -= OnWindowKeyUp;
        Deactivated -= OnWindowDeactivated;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
        base.OnClosed(e);
    }

    private void CloseDeskWindows()
    {
        CloseDeskWindow(_sstvDeskWindow);
        CloseDeskWindow(_voiceDeskWindow);
        CloseDeskWindow(_cwDeskWindow);
        CloseDeskWindow(_rttyDeskWindow);
        CloseDeskWindow(_wefaxDeskWindow);
        CloseDeskWindow(_wsjtxDeskWindow);
        CloseDeskWindow(_js8DeskWindow);
        CloseDeskWindow(_longwaveDeskWindow);

        _sstvDeskWindow = null;
        _voiceDeskWindow = null;
        _cwDeskWindow = null;
        _rttyDeskWindow = null;
        _wefaxDeskWindow = null;
        _wsjtxDeskWindow = null;
        _js8DeskWindow = null;
        _longwaveDeskWindow = null;
    }

    private void CloseDeskWindow(Window? window)
    {
        if (window is null || !window.IsVisible || !_isClosing)
        {
            return;
        }

        window.Close();
    }
}
