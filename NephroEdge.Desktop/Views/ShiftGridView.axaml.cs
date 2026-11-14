using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NephroEdge.Desktop.ViewModels;

namespace NephroEdge.Desktop.Views;

/// <summary>
/// Code-behind for Workspace 1 (Active 12-Bed Shift Risk Grid).
/// Wires responsive bed card widths, keyboard search interactions (Esc / Enter),
/// and whole-card selection clicks without duplicating business logic.
/// </summary>
public partial class ShiftGridView : UserControl
{
    public ShiftGridView()
    {
        InitializeComponent();

        ShiftGridScrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        SearchFilterTextBox.KeyDown += OnSearchFilterKeyDown;
    }

    private void OnScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ShiftGridViewModel vm)
        {
            vm.UpdateCardWidthForViewport(e.NewSize.Width);
        }
    }

    private void OnSearchFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShiftGridViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            vm.OpenFirstMatchingBedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnBedCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual visual && visual.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        if (sender is Control { DataContext: ShiftBedTileItem tile } &&
            DataContext is ShiftGridViewModel vm)
        {
            vm.SelectBedCommand.Execute(tile);
        }
    }
}
