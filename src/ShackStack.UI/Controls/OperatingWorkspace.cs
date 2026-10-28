using Avalonia;
using Avalonia.Controls;
using Avalonia.Metadata;

namespace ShackStack.UI.Controls;

public partial class OperatingWorkspace : UserControl
{
    public static readonly StyledProperty<Control?> WorkspaceContentProperty =
        AvaloniaProperty.Register<OperatingWorkspace, Control?>(nameof(WorkspaceContent));

    public static readonly StyledProperty<string> DeskProperty =
        AvaloniaProperty.Register<OperatingWorkspace, string>(nameof(Desk), "Operate");

    public static readonly StyledProperty<bool> ShowScopeProperty =
        AvaloniaProperty.Register<OperatingWorkspace, bool>(nameof(ShowScope), true);

    public bool ShowScope
    {
        get => GetValue(ShowScopeProperty);
        set => SetValue(ShowScopeProperty, value);
    }

    [Content]
    public Control? WorkspaceContent
    {
        get => GetValue(WorkspaceContentProperty);
        set => SetValue(WorkspaceContentProperty, value);
    }

    public string Desk
    {
        get => GetValue(DeskProperty);
        set => SetValue(DeskProperty, value);
    }

    public OperatingWorkspace() => InitializeComponent();
}
