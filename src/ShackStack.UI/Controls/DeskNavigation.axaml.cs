using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShackStack.UI.Views;

namespace ShackStack.UI.Controls;

public partial class DeskNavigation : UserControl
{
    public static readonly StyledProperty<string> DeskProperty =
        AvaloniaProperty.Register<DeskNavigation, string>(nameof(Desk), "Operate");

    public string Desk
    {
        get => GetValue(DeskProperty);
        set => SetValue(DeskProperty, value);
    }

    public DeskNavigation()
    {
        InitializeComponent();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == DeskProperty)
                UpdateSelection();
        };
        AttachedToVisualTree += (_, _) => UpdateSelection();
    }

    private void UpdateSelection()
    {
        foreach (var button in this.FindControl<StackPanel>("DeskButtons")!.Children.OfType<Button>())
            button.Classes.Set("currentDesk", string.Equals(button.Tag as string, Desk, StringComparison.Ordinal));
    }

    private void OnNavigate(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string desk })
            return;

        var window = TopLevel.GetTopLevel(this) as Window;
        var shell = window as MainWindow ?? window?.Owner as MainWindow;
        shell?.OpenDesk(desk);
    }
}
