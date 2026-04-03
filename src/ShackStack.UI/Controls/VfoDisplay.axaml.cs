using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ShackStack.UI.Controls;

public partial class VfoDisplay : UserControl
{
    public static readonly StyledProperty<long> FrequencyProperty =
        AvaloniaProperty.Register<VfoDisplay, long>(nameof(Frequency), 14_175_000);

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<VfoDisplay, bool>(nameof(IsActive));

    public static readonly StyledProperty<ICommand?> TuneStepCommandProperty =
        AvaloniaProperty.Register<VfoDisplay, ICommand?>(nameof(TuneStepCommand));

    private readonly Border[] _digitBorders;
    private readonly TextBlock[] _digitTexts;
    private readonly Border _rootBorder;

    public long Frequency
    {
        get => GetValue(FrequencyProperty);
        set => SetValue(FrequencyProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public ICommand? TuneStepCommand
    {
        get => GetValue(TuneStepCommandProperty);
        set => SetValue(TuneStepCommandProperty, value);
    }

    public VfoDisplay()
    {
        InitializeComponent();

        _rootBorder = this.FindControl<Border>("RootBorder")!;

        _digitBorders =
        [
            this.FindControl<Border>("Digit0")!,
            this.FindControl<Border>("Digit1")!,
            this.FindControl<Border>("Digit2")!,
            this.FindControl<Border>("Digit3")!,
            this.FindControl<Border>("Digit4")!,
            this.FindControl<Border>("Digit5")!,
            this.FindControl<Border>("Digit6")!,
            this.FindControl<Border>("Digit7")!,
        ];

        _digitTexts =
        [
            this.FindControl<TextBlock>("Digit0Text")!,
            this.FindControl<TextBlock>("Digit1Text")!,
            this.FindControl<TextBlock>("Digit2Text")!,
            this.FindControl<TextBlock>("Digit3Text")!,
            this.FindControl<TextBlock>("Digit4Text")!,
            this.FindControl<TextBlock>("Digit5Text")!,
            this.FindControl<TextBlock>("Digit6Text")!,
            this.FindControl<TextBlock>("Digit7Text")!,
        ];

        FrequencyProperty.Changed.AddClassHandler<VfoDisplay>((control, _) => control.UpdateDigits());
        IsActiveProperty.Changed.AddClassHandler<VfoDisplay>((control, _) => control.UpdateActiveState());
        UpdateDigits();
        UpdateActiveState();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void UpdateDigits()
    {
        var hz = Math.Clamp(Frequency, 100_000L, 450_000_000L);
        var mhz = hz / 1_000_000;
        var khz = (hz % 1_000_000) / 1_000;
        var sub = hz % 1_000;
        var digits = $"{mhz,2}{khz:000}{sub:000}";

        for (var i = 0; i < _digitTexts.Length && i < digits.Length; i++)
        {
            _digitTexts[i].Text = digits[i].ToString();
        }
    }

    private void UpdateActiveState()
    {
        if (IsActive)
        {
            _rootBorder.BorderBrush = new SolidColorBrush(Color.Parse("#2EA26A"));
            _rootBorder.Background = new SolidColorBrush(Color.Parse("#0D1712"));
        }
        else
        {
            _rootBorder.BorderBrush = new SolidColorBrush(Color.Parse("#1E2128"));
            _rootBorder.Background = new SolidColorBrush(Color.Parse("#0B0E13"));
        }
    }

    private void DigitWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string stepText || !long.TryParse(stepText, out var step))
        {
            return;
        }

        var delta = e.Delta.Y > 0 ? step : -step;
        if (TuneStepCommand?.CanExecute(delta) == true)
        {
            TuneStepCommand.Execute(delta);
        }
    }

    private void DigitPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.Parse("#1A1D2E"));
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        }
    }

    private void DigitPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Arrow);
        }
    }
}
