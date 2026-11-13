using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NephroEdge.Desktop.Infrastructure;
using NephroEdge.Desktop.ViewModels;

namespace NephroEdge.Desktop;

/// <summary>
/// Primary desktop shell window hosting left-rail navigation, global clinical keyboard shortcuts,
/// the IPSG.1 Sticky Patient Banner, and all four NephroEdge Doctor Workstation views.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnClosed(EventArgs e)
    {
        RemoveHandler(KeyDownEvent, OnWindowKeyDown);

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.Key == Key.F5)
        {
            vm.RefreshCurrentWorkspace();
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.D1:
            case Key.NumPad1:
                vm.SelectWorkspaceTab(WorkspaceTab.ShiftGrid);
                e.Handled = true;
                break;

            case Key.D2:
            case Key.NumPad2:
                vm.SelectWorkspaceTab(WorkspaceTab.PatientAnalytics);
                e.Handled = true;
                break;

            case Key.D3:
            case Key.NumPad3:
                vm.SelectWorkspaceTab(WorkspaceTab.SafetyRxWorkbench);
                e.Handled = true;
                break;

            case Key.D4:
            case Key.NumPad4:
                vm.SelectWorkspaceTab(WorkspaceTab.SyncMonitor);
                e.Handled = true;
                break;

            case Key.R:
                vm.RefreshCurrentWorkspace();
                e.Handled = true;
                break;
        }
    }

    private void OnShellSyncCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectWorkspaceTab(WorkspaceTab.SyncMonitor);
        }
    }
}
