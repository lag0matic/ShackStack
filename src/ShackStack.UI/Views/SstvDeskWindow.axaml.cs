using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ShackStack.UI.ViewModels;

namespace ShackStack.UI.Views;

public partial class SstvDeskWindow : Window
{
    private bool _isDraggingOverlay;
    private SstvOverlayItemViewModel? _dragOverlayItem;
    private Point _dragStartPoint;
    private double _dragStartX;
    private double _dragStartY;

    public SstvDeskWindow()
    {
        InitializeComponent();
    }

    private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control
            || control.DataContext is not SstvOverlayItemViewModel item
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDraggingOverlay = true;
        _dragOverlayItem = item;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedSstvReplyOverlayItem = item;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragStartX = item.X;
        _dragStartY = item.Y;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingOverlay || _dragOverlayItem is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var dx = point.X - _dragStartPoint.X;
        var dy = point.Y - _dragStartPoint.Y;
        _dragOverlayItem.X = Math.Max(0, _dragStartX + dx);
        _dragOverlayItem.Y = Math.Max(0, _dragStartY + dy);
        e.Handled = true;
    }

    private void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is InputElement element)
        {
            e.Pointer.Capture(null);
        }

        ResetOverlayDrag();
        e.Handled = true;
    }

    private void OnOverlayPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ResetOverlayDrag();
    }

    private void ResetOverlayDrag()
    {
        _isDraggingOverlay = false;
        _dragOverlayItem = null;
    }
}
