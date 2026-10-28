using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace ShackStack.UI.Controls;

public partial class ContactLogPanel : UserControl
{
    public static readonly StyledProperty<ICommand?> UseRigCommandProperty =
        AvaloniaProperty.Register<ContactLogPanel, ICommand?>(nameof(UseRigCommand));

    public ICommand? UseRigCommand
    {
        get => GetValue(UseRigCommandProperty);
        set => SetValue(UseRigCommandProperty, value);
    }

    public ContactLogPanel() => InitializeComponent();
}
